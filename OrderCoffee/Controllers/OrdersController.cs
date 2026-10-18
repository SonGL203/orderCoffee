using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;
using OrderCoffee.Services;

namespace OrderCoffee.Controllers;

// Order online: khách có tài khoản hoặc khách vãng lai (nhập tên, SĐT, địa chỉ) đều đặt được.
// Khách vãng lai theo dõi đơn bằng link chứa mã đơn; voucher và điểm chỉ dành cho khách đã đăng nhập.
public class OrdersController(AppDbContext db, OrderService orders, PromotionService promo, IConfiguration config, EventHub hub) : Controller
{
    private int? CustomerId => User.IsInRole(Roles.Customer) ? User.GetUserId() : null;

    private async Task LoadCheckoutExtras(int subTotal, VoucherCheck? chk)
    {
        ViewBag.Cart = HttpContext.Session.GetCart();
        ViewBag.SubTotal = subTotal;
        ViewBag.Check = chk;
        var uid = CustomerId;
        ViewBag.Wallet = uid == null ? new List<UserVoucher>()
            : await db.UserVouchers.Include(x => x.Voucher)
                .Where(x => x.UserId == uid && !x.IsUsed && x.Voucher!.EndAt >= DateTime.Today).ToListAsync();
        ViewBag.Public = await db.Vouchers.Where(v => v.IsActive && v.PointCost == 0 && v.EndAt >= DateTime.Today).ToListAsync();
        ViewBag.Addresses = uid == null ? new List<UserAddress>()
            : await db.UserAddresses.Where(a => a.UserId == uid).OrderByDescending(a => a.LastUsedAt).Take(6).ToListAsync();
    }

    [HttpGet]
    public async Task<IActionResult> Checkout()
    {
        var cart = HttpContext.Session.GetCart();
        if (cart.Count == 0) return RedirectToAction("Index", "Cart");
        var vm = new CheckoutVm();
        if (CustomerId is { } uid)
        {
            var u = await db.Users.FindAsync(uid);
            vm.CustomerName = u!.FullName;
            vm.Phone = u.Phone;
            var last = await db.UserAddresses.Where(a => a.UserId == uid).OrderByDescending(a => a.LastUsedAt).FirstOrDefaultAsync();
            if (last != null) { vm.Phone = last.Phone; vm.Address = last.Address; vm.CustomerName = last.ContactName ?? vm.CustomerName; }
        }
        await LoadCheckoutExtras(cart.Sum(c => c.LineTotal), null);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutVm vm, string? action)
    {
        var cart = HttpContext.Session.GetCart();
        if (cart.Count == 0) return RedirectToAction("Index", "Cart");
        var uid = CustomerId;

        var (items, error) = await orders.BuildItemsAsync(cart);
        if (error != null) ModelState.AddModelError("", error);
        var subTotal = items.Sum(i => i.LineTotal);
        var chk = await promo.CheckAsync(vm.VoucherCode, uid, subTotal);
        if (chk.Error != null) ModelState.AddModelError(nameof(vm.VoucherCode), chk.Error);

        if (!ModelState.IsValid || action == "preview")
        {
            if (action == "preview") { ModelState.Remove(nameof(vm.Address)); ModelState.Remove(nameof(vm.Phone)); }
            await LoadCheckoutExtras(subTotal, chk);
            return View(vm);
        }

        var order = new Order
        {
            Type = OrderType.Online, Status = OrderStatus.Pending, UserId = uid,
            DeliveryType = DeliveryType.Delivery, CustomerName = vm.CustomerName?.Trim(), Phone = vm.Phone,
            Address = vm.Address.Trim(),
            Note = vm.Note?.Trim(), PaymentMethod = vm.PaymentMethod
        };
        order.Items.AddRange(items);
        PromotionService.Recalc(order, null);
        promo.Apply(order, chk);
        db.Orders.Add(order);
        if (uid != null) await SaveAddressAsync(uid.Value, order);
        await db.SaveChangesAsync();

        HttpContext.Session.ClearCart();
        HttpContext.Session.SetString("LastOrderCode", order.Code);
        TempData["Success"] = "Đặt hàng thành công! Quán sẽ xác nhận đơn của bạn trong ít phút. Hãy lưu lại link trang này để theo dõi đơn.";
        return RedirectToAction(nameof(Details), new { code = order.Code });
    }

    // Lưu (hoặc cập nhật lần dùng) địa chỉ + SĐT vào sổ địa chỉ của khách đã đăng ký
    private async Task SaveAddressAsync(int uid, Order order)
    {
        var existing = await db.UserAddresses.FirstOrDefaultAsync(a => a.UserId == uid && a.Phone == order.Phone && a.Address == order.Address);
        if (existing == null)
            db.UserAddresses.Add(new UserAddress { UserId = uid, Phone = order.Phone!, Address = order.Address!, ContactName = order.CustomerName, UseCount = 1 });
        else
        {
            existing.UseCount++;
            existing.LastUsedAt = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(order.CustomerName)) existing.ContactName = order.CustomerName;
        }
    }

    [Authorize(Roles = Roles.Customer)]
    public async Task<IActionResult> Index()
    {
        var uid = CustomerId;
        var list = await db.Orders.Include(o => o.Items).Where(o => o.UserId == uid)
            .OrderByDescending(o => o.Id).ToListAsync();
        return View(list);
    }

    [HttpGet("Orders/Details/{code}")]
    public async Task<IActionResult> Details(string code)
    {
        var o = await db.Orders.Include(x => x.Items).Include(x => x.Voucher).FirstOrDefaultAsync(x => x.Code == code);
        if (o == null) return NotFound();
        ViewBag.Bank = config.GetSection("Shop");
        ViewBag.IsOwner = CustomerId != null && o.UserId == CustomerId;
        return View(o);
    }

    // SSE: trang theo dõi đơn tự cập nhật khi quán đổi trạng thái
    [HttpGet("Orders/Stream/{code}")]
    public async Task Stream(string code) => await Sse.StreamAsync(HttpContext, hub, "order:" + code);

    // Polling dự phòng cho trang theo dõi đơn
    [HttpGet("Orders/Status/{code}")]
    public async Task<IActionResult> Status(string code)
    {
        var o = await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code);
        if (o == null) return NotFound();
        return Json(new { status = o.Status.ToString(), text = Fmt.Status(o.Status), color = Fmt.StatusColor(o.Status), paid = o.PaymentStatus == PaymentStatus.Paid });
    }

    [HttpPost("Orders/Cancel/{code}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string code)
    {
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Code == code);
        if (o == null) return NotFound();
        if (o.Status != OrderStatus.Pending)
        {
            TempData["Error"] = "Đơn đã được quán xác nhận, không thể hủy. Vui lòng liên hệ quán.";
            return RedirectToAction(nameof(Details), new { code });
        }
        await orders.CancelAsync(o, "Khách hủy đơn");
        TempData["Success"] = "Đã hủy đơn. Voucher (nếu có) đã được hoàn lại.";
        return RedirectToAction(nameof(Details), new { code });
    }

    [Authorize(Roles = Roles.Customer), HttpPost("Orders/Reorder/{code}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(string code)
    {
        var uid = CustomerId;
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Code == code && x.UserId == uid);
        if (o == null) return NotFound();
        var cart = new List<CartItem>();
        foreach (var i in o.Items)
        {
            var p = await db.Products.FindAsync(i.ProductId);
            if (p is { IsAvailable: true })
                cart.Add(new CartItem { ProductId = p.Id, Name = p.Name, Price = p.Price, Quantity = i.Quantity, Note = i.Note });
        }
        HttpContext.Session.SaveCart(cart);
        return RedirectToAction("Index", "Cart");
    }
}
