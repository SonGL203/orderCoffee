using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;
using OrderCoffee.Services;

namespace OrderCoffee.Controllers;

[Authorize(Roles = Roles.AdminOrStaff)]
public class StaffController(AppDbContext db, OrderService orders, EventHub hub) : Controller
{
    private static readonly OrderStatus[] ActiveOnline =
        [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Delivering];

    public async Task<IActionResult> Index()
    {
        var tables = await db.Tables.OrderBy(t => t.Id).ToListAsync();
        var open = await db.Orders.Include(o => o.Items)
            .Where(o => o.Type == OrderType.DineIn && (o.Status == OrderStatus.Open || o.Status == OrderStatus.WaitingPayment))
            .ToListAsync();
        ViewBag.Tables = tables;
        ViewBag.OpenOrders = open.Where(o => o.TableId != null).ToDictionary(o => o.TableId!.Value);
        ViewBag.PendingOnline = await db.Orders.CountAsync(o => o.Type == OrderType.Online && o.Status == OrderStatus.Pending);
        ViewBag.ActiveOnline = await db.Orders.CountAsync(o => o.Type == OrderType.Online && ActiveOnline.Contains(o.Status));
        return View();
    }

    // SSE: đẩy "changed" cho nhân viên mỗi khi đơn/món/bàn thay đổi
    [HttpGet]
    public async Task Stream() => await Sse.StreamAsync(HttpContext, hub, "staff");

    // Trạng thái cần chú ý, trình duyệt của nhân viên gọi định kỳ để báo đơn mới
    [HttpGet]
    public async Task<IActionResult> Alerts()
    {
        var online = await db.Orders.AsNoTracking()
            .Where(o => o.Type == OrderType.Online && o.Status == OrderStatus.Pending)
            .OrderBy(o => o.Id).Select(o => new { o.Id, o.Phone, o.Total }).ToListAsync();

        // Món chưa ra của các bàn đang phục vụ (maxItemId tăng = có món mới được gọi)
        var items = await db.OrderItems.AsNoTracking()
            .Where(i => !i.IsServed && i.Order!.Type == OrderType.DineIn
                && (i.Order.Status == OrderStatus.Open || i.Order.Status == OrderStatus.WaitingPayment))
            .GroupBy(i => new { i.Order!.TableId, TableName = i.Order.Table!.Name })
            .Select(g => new { tableId = g.Key.TableId, name = g.Key.TableName, maxItemId = g.Max(i => i.Id), count = g.Sum(i => i.Quantity) })
            .ToListAsync();

        var payments = await db.Orders.AsNoTracking()
            .Where(o => o.Type == OrderType.DineIn && o.Status == OrderStatus.WaitingPayment)
            .Select(o => new { orderId = o.Id, tableId = o.TableId, name = o.Table!.Name, o.Total }).ToListAsync();

        return Json(new { online, items, payments });
    }

    // ---------- Bàn ----------
    public async Task<IActionResult> Table(int id)
    {
        var t = await db.Tables.FindAsync(id);
        if (t == null) return NotFound();
        ViewBag.Products = await db.Products.Where(p => p.IsAvailable).OrderBy(p => p.Name).ToListAsync();
        ViewBag.Table = t;
        ViewBag.Order = await orders.GetOpenTableOrderAsync(id);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ServeItem(int itemId, bool served = true)
    {
        var item = await db.OrderItems.Include(i => i.Order).FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null) return NotFound();
        item.IsServed = served;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Table), new { id = item.Order!.TableId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ServeAll(int orderId)
    {
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound();
        foreach (var i in o.Items) i.IsServed = true;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Table), new { id = o.TableId });
    }

    // Nhân viên gọi món hộ khách (khách không dùng điện thoại)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(int tableId, int productId, int quantity = 1, string? note = null)
    {
        var t = await db.Tables.FindAsync(tableId);
        if (t == null) return NotFound();
        var cart = new List<CartItem> { new() { ProductId = productId, Quantity = Math.Max(1, quantity), Note = note } };
        var (_, err) = await orders.AddToTableAsync(t, cart, null);
        if (err != null) TempData["Error"] = err; else TempData["Success"] = "Đã thêm món vào bàn.";
        return RedirectToAction(nameof(Table), new { id = tableId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(int orderId, PaymentMethod method)
    {
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound();
        if (o.Status is OrderStatus.Completed or OrderStatus.Cancelled) return RedirectToAction(nameof(Index));
        if (o.Items.Any(i => !i.IsServed))
        {
            TempData["Error"] = "Còn món chưa ra, hãy ra đủ món rồi mới xác nhận thanh toán.";
            return RedirectToAction(nameof(Table), new { id = o.TableId });
        }
        await orders.CompleteAsync(o, User.GetUserId(), method);
        TempData["Success"] = $"Đã xác nhận thanh toán đơn #{o.Id}. Bàn đã chuyển về trạng thái trống.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelTableOrder(int orderId)
    {
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound();
        await orders.CancelAsync(o, "Nhân viên hủy");
        TempData["Success"] = "Đã hủy đơn, bàn đã trống.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetTable(int id)
    {
        var t = await db.Tables.FindAsync(id);
        if (t == null) return NotFound();
        var hasOpen = await orders.GetOpenTableOrderAsync(id) != null;
        if (hasOpen) TempData["Error"] = "Bàn còn đơn chưa thanh toán.";
        else { t.Status = TableStatus.Available; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }

    // ---------- Tạo đơn (nhân viên) ----------
    [HttpGet]
    public async Task<IActionResult> Create(int? tableId)
    {
        await LoadCreateLists();
        ViewBag.TableId = tableId;
        return View();
    }

    private async Task LoadCreateLists()
    {
        ViewBag.Tables = await db.Tables.OrderBy(t => t.Id).ToListAsync();
        ViewBag.Categories = await db.Categories.Include(c => c.Products).OrderBy(c => c.SortOrder).ToListAsync();
    }

    // kind: table | counter (mang đi / uống tại quầy) | delivery (khách gọi điện, giao tận nơi)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string kind, int? tableId, string? customerName, string? phone, string? address, string? note, PaymentMethod paymentMethod = PaymentMethod.Cash)
    {
        var cart = new List<CartItem>();
        foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("qty_")))
            if (int.TryParse(key[4..], out var pid) && int.TryParse(Request.Form[key], out var qty) && qty > 0)
                cart.Add(new CartItem { ProductId = pid, Quantity = Math.Min(qty, 50), Note = Request.Form["note_" + pid].ToString().Trim() is { Length: > 0 } n ? n : null });

        string? Fail(string msg) { TempData["Error"] = msg; return msg; }
        IActionResult Back(string msg) { Fail(msg); return RedirectToAction(nameof(Create), new { tableId }); }

        if (cart.Count == 0) return Back("Chưa chọn món nào.");

        // SĐT thành viên (nếu có) -> gắn tài khoản để tích điểm
        phone = phone?.Trim();
        var member = string.IsNullOrEmpty(phone) ? null : await db.Users.FirstOrDefaultAsync(u => u.Phone == phone && u.Role == Roles.Customer);

        if (kind == "table")
        {
            var table = tableId == null ? null : await db.Tables.FindAsync(tableId);
            if (table == null) return Back("Hãy chọn bàn.");
            var (_, err) = await orders.AddToTableAsync(table, cart, member?.Id);
            if (err != null) return Back(err);
            TempData["Success"] = $"Đã thêm món cho {table.Name}.";
            return RedirectToAction(nameof(Table), new { id = table.Id });
        }

        var isDelivery = kind == "delivery";
        if (isDelivery && (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(phone)))
            return Back("Đơn giao hàng cần số điện thoại và địa chỉ.");

        var (items, error) = await orders.BuildItemsAsync(cart);
        if (error != null) return Back(error);

        var order = new Order
        {
            Type = OrderType.Online, Status = OrderStatus.Confirmed, UserId = member?.Id,
            DeliveryType = isDelivery ? DeliveryType.Delivery : DeliveryType.Pickup,
            CustomerName = string.IsNullOrWhiteSpace(customerName) ? (member?.FullName ?? "Khách tại quầy") : customerName.Trim(),
            Phone = phone, Address = isDelivery ? address!.Trim() : null, Note = note?.Trim(),
            PaymentMethod = paymentMethod, HandledByStaffId = User.GetUserId()
        };
        order.Items.AddRange(items);
        PromotionService.Recalc(order, null);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        TempData["Success"] = $"Đã tạo đơn #{order.Id} ({(isDelivery ? "giao hàng" : "mang đi/tại quầy")}). Theo dõi ở mục Đơn online." + (member != null ? $" Đã gắn thành viên {member.FullName}." : "");
        return RedirectToAction(nameof(Online));
    }

    // ---------- Online ----------
    public async Task<IActionResult> Online(string? tab)
    {
        var q = db.Orders.Include(o => o.Items).Include(o => o.User).Where(o => o.Type == OrderType.Online);
        ViewBag.Tab = tab ?? "active";
        q = tab == "done"
            ? q.Where(o => o.Status == OrderStatus.Completed || o.Status == OrderStatus.Cancelled)
            : q.Where(o => ActiveOnline.Contains(o.Status));
        var list = await q.OrderByDescending(o => o.Id).Take(100).ToListAsync();
        return View(list);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Advance(int id)
    {
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound();
        var next = OrderService.NextOnlineStatus(o);
        if (next == null) return RedirectToAction(nameof(Online));
        if (next == OrderStatus.Completed) await orders.CompleteAsync(o, User.GetUserId());
        else
        {
            o.Status = next.Value;
            o.HandledByStaffId = User.GetUserId();
            o.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Online), new { tab = Request.Form["tab"].ToString() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectOnline(int id, string? reason)
    {
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound();
        if (o.Status is OrderStatus.Completed or OrderStatus.Cancelled) return RedirectToAction(nameof(Online));
        await orders.CancelAsync(o, string.IsNullOrWhiteSpace(reason) ? "Quán từ chối đơn" : reason.Trim());
        return RedirectToAction(nameof(Online));
    }

    public async Task<IActionResult> OrderDetail(int id)
    {
        var o = await db.Orders.Include(x => x.Items).Include(x => x.User).Include(x => x.Table).Include(x => x.Voucher)
            .FirstOrDefaultAsync(x => x.Id == id);
        return o == null ? NotFound() : View(o);
    }

    public async Task<IActionResult> History()
    {
        var list = await db.Orders.Include(o => o.Table).Include(o => o.User)
            .Where(o => o.Status == OrderStatus.Completed || o.Status == OrderStatus.Cancelled)
            .OrderByDescending(o => o.UpdatedAt).Take(100).ToListAsync();
        return View(list);
    }
}
