using System.Collections.Concurrent;
using System.Threading.Channels;

namespace OrderCoffee.Services;

/// <summary>
/// Pub/sub trong bộ nhớ cho Server-Sent Events. Mỗi kết nối SSE đăng ký một "topic":
///   "staff"        – mọi thay đổi đơn/bàn (cho nhân viên)
///   "order:{code}" – đơn online cụ thể (khách theo dõi đơn)
///   "table:{id}"   – bàn cụ thể (khách đang gọi món tại bàn)
/// Chạy một instance duy nhất (singleton); nếu sau này chạy nhiều server thì thay bằng Redis pub/sub.
/// </summary>
public class EventHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<string>>> _topics = new();

    public Subscription Subscribe(string topic)
    {
        var id = Guid.NewGuid();
        // Chỉ cần biết "có thay đổi"; tin cũ bị bỏ nếu client đọc chậm
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });
        _topics.GetOrAdd(topic, _ => new()).TryAdd(id, channel);
        return new Subscription(channel.Reader, () =>
        {
            if (_topics.TryGetValue(topic, out var subs)) subs.TryRemove(id, out _);
            channel.Writer.TryComplete();
        });
    }

    public void Publish(string topic, string data = "changed")
    {
        if (!_topics.TryGetValue(topic, out var subs)) return;
        foreach (var ch in subs.Values) ch.Writer.TryWrite(data);
    }

    public sealed class Subscription(ChannelReader<string> reader, Action onDispose) : IDisposable
    {
        public ChannelReader<string> Reader { get; } = reader;
        public void Dispose() => onDispose();
    }
}

public static class Sse
{
    /// <summary>Giữ kết nối mở và đẩy sự kiện "changed" mỗi khi topic có tin; gửi ping 15 giây/lần để không bị ngắt.</summary>
    public static async Task StreamAsync(HttpContext ctx, EventHub hub, string topic)
    {
        var res = ctx.Response;
        res.Headers.ContentType = "text/event-stream";
        res.Headers.CacheControl = "no-cache";
        res.Headers["X-Accel-Buffering"] = "no";
        ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

        using var sub = hub.Subscribe(topic);
        var ct = ctx.RequestAborted;
        try
        {
            await res.WriteAsync("retry: 3000\n: connected\n\n", ct);
            await res.Body.FlushAsync(ct);
            while (!ct.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var data = await sub.Reader.ReadAsync(timeout.Token);
                    await res.WriteAsync($"event: changed\ndata: {data}\n\n", ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await res.WriteAsync(": ping\n\n", ct);   // keep-alive
                }
                await res.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }   // khách đóng trang
        catch (ChannelClosedException) { }
    }
}
