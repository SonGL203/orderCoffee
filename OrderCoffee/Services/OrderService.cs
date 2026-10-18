using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;

namespace OrderCoffee.Services;

public class OrderService(AppDbContext db, PromotionService promo)
{
    /// <summary>Tạo OrderItem từ giỏ hàng, lấy giá mới nhất từ DB (không tin giá trong session).</summary>
    public async Task<(List<OrderItem> Items, string? Error)> BuildItemsAsync(List<CartItem> cart, int batch = 1)
    {
        if (cart.Count == 0) return (new(), "Giỏ hàng đang trống.");
        var ids = cart.Select(c => c.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var items = new List<OrderItem>();
        foreach (var c in cart)
        {
            if (!products.TryGetValue(c.ProductId, out var p) || !p.IsAvailable)
                return (new(), $"Món \"{c.Name}\" hiện đã hết, vui lòng bỏ khỏi giỏ.");
            items.Add(new OrderItem
            {
                ProductId = p.Id, ProductName = p.Name, UnitPrice = p.Price,
                Quantity = Math.Clamp(c.Quantity, 1, 50), Note = c.Note, Batch = batch
            });
        }
        return (items, null);
    }

    public Task<Order?> GetOpenTableOrderAsync(int tableId) =>
        db.Orders.Include(o => o.Items).Include(o => o.Voucher).Include(o => o.User)
            .FirstOrDefaultAsync(o => o.TableId == tableId && o.Type == OrderType.DineIn
                && (o.Status == OrderStatus.Open || o.Status == OrderStatus.WaitingPayment));

    /// <summary>Thêm món vào đơn đang mở của bàn (tạo đơn mới nếu bàn trống).</summary>
    public async Task<(Order? Order, string? Error)> AddToTableAsync(CafeTable table, List<CartItem> cart, int? userId)
    {
        var order = await GetOpenTableOrderAsync(table.Id);
        var batch = order == null ? 1 : order.Items.Select(i => i.Batch).DefaultIfEmpty(0).Max() + 1;
        var (items, error) = await BuildItemsAsync(cart, batch);
        if (error != null) return (null, error);

        if (order == null)
        {
            order = new Order { Type = OrderType.DineIn, Status = OrderStatus.Open, TableId = table.Id, UserId = userId };
            db.Orders.Add(order);
        }
        else if (order.Status == OrderStatus.WaitingPayment)
        {
            // Khách gọi thêm sau khi đã yêu cầu thanh toán -> quay lại phục vụ
            order.Status = OrderStatus.Open;
        }
        order.UserId ??= userId;
        order.Items.AddRange(items);
        order.UpdatedAt = DateTime.Now;
        PromotionService.Recalc(order, order.Voucher);
        table.Status = order.Status == OrderStatus.Open ? TableStatus.Occupied : TableStatus.WaitingPayment;
        await db.SaveChangesAsync();
        return (order, null);
    }

    /// <summary>Đổi/gỡ voucher cho đơn chưa thanh toán.</summary>
    public async Task<string?> ChangeVoucherAsync(Order order, string? code)
    {
        await promo.ReleaseAsync(order);
        if (!string.IsNullOrWhiteSpace(code))
        {
            var chk = await promo.CheckAsync(code, order.UserId, order.Items.Sum(i => i.LineTotal), order.Id);
            if (!chk.Ok) { await db.SaveChangesAsync(); return chk.Error; }
            promo.Apply(order, chk);
            order.Voucher = chk.Voucher;
        }
        order.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Nhân viên xác nhận đã thu tiền -> hoàn thành đơn, cộng điểm, trả bàn.</summary>
    public async Task CompleteAsync(Order order, int? staffId, PaymentMethod? method = null)
    {
        if (method.HasValue) order.PaymentMethod = method.Value;
        order.Status = OrderStatus.Completed;
        order.PaymentStatus = PaymentStatus.Paid;
        order.PaidAt ??= DateTime.Now;
        order.HandledByStaffId = staffId ?? order.HandledByStaffId;
        order.UpdatedAt = DateTime.Now;
        await promo.AwardPointsAsync(order);
        if (order.TableId != null)
        {
            var table = await db.Tables.FindAsync(order.TableId);
            if (table != null) table.Status = TableStatus.Available;
        }
        await db.SaveChangesAsync();
    }

    public async Task CancelAsync(Order order, string? reason)
    {
        await promo.ReleaseAsync(order);
        order.Status = OrderStatus.Cancelled;
        order.CancelReason = reason;
        order.UpdatedAt = DateTime.Now;
        if (order.TableId != null)
        {
            var table = await db.Tables.FindAsync(order.TableId);
            if (table != null) table.Status = TableStatus.Available;
        }
        await db.SaveChangesAsync();
    }

    public static OrderStatus? NextOnlineStatus(Order o) => o.Status switch
    {
        OrderStatus.Pending => OrderStatus.Confirmed,
        OrderStatus.Confirmed => OrderStatus.Preparing,
        OrderStatus.Preparing => o.DeliveryType == DeliveryType.Delivery ? OrderStatus.Delivering : OrderStatus.Ready,
        OrderStatus.Ready or OrderStatus.Delivering => OrderStatus.Completed,
        _ => null
    };
}
