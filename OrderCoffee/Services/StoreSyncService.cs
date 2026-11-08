using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;

namespace OrderCoffee.Services;

// Tìm chi nhánh thương hiệu trên bản đồ mở OpenStreetMap (Nominatim, miễn phí, không cần API key)
// rồi lưu xuống bảng Stores. Nominatim yêu cầu tối đa 1 request/giây và phải có User-Agent.
public class StoreSyncService(AppDbContext db, IHttpClientFactory http, IConfiguration config, ILogger<StoreSyncService> log)
{
    public async Task<(int Added, int Updated)> SyncAsync(CancellationToken ct = default)
    {
        var brand = config["Stores:Brand"] ?? "Highlands Coffee";
        var areas = config.GetSection("Stores:Areas").Get<string[]>() ?? ["Hồ Chí Minh", "Hà Nội", "Đà Nẵng"];
        var client = http.CreateClient("nominatim");
        var existing = await db.Stores.ToDictionaryAsync(s => s.OsmKey, ct);
        int added = 0, updated = 0;

        foreach (var area in areas)
        {
            var url = "search?format=jsonv2&addressdetails=1&limit=40&countrycodes=vn&q=" + Uri.EscapeDataString($"{brand} {area}");
            try
            {
                using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    var name = e.GetProperty("name").GetString() ?? "";
                    if (!name.Contains(brand.Split(' ')[0], StringComparison.OrdinalIgnoreCase)) continue;
                    var key = e.GetProperty("osm_type").GetString() + "/" + e.GetProperty("osm_id").GetInt64();
                    var display = e.GetProperty("display_name").GetString() ?? "";
                    var address = display.StartsWith(name) ? display[name.Length..].TrimStart(',', ' ') : display;
                    var a = e.TryGetProperty("address", out var ad) ? ad : default;
                    string Get(string p) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(p, out var v) ? v.GetString() ?? "" : "";
                    var city = new[] { Get("city"), Get("state"), Get("province") }.FirstOrDefault(x => x != "") ?? area;

                    if (!existing.TryGetValue(key, out var s))
                    {
                        db.Stores.Add(existing[key] = s = new Store { OsmKey = key });
                        added++;
                    }
                    else updated++;
                    s.Name = name;
                    s.Address = address.Length > 500 ? address[..500] : address;
                    s.City = city;
                    s.Lat = double.Parse(e.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
                    s.Lng = double.Parse(e.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
                    s.SyncedAt = DateTime.Now;
                }
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                log.LogWarning(ex, "Đồng bộ cửa hàng khu vực {Area} lỗi", area);
            }
            await Task.Delay(1100, ct);
        }
        return (added, updated);
    }
}
