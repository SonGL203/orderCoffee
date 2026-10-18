using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;
using OrderCoffee.Services;

namespace OrderCoffee.Controllers;

// Order offline: khách quét QR trên bàn (không cần đăng nhập)
[Route("t/{code}")]
public class TableController(AppDbContext db, OrderService orders, EventHub hub) : Controller
{
    private async Task<CafeTable?> FindTable(string code) =>
        await db.Tables.FirstOrDefaultAsync(t => t.Code == code);

    [HttpGet("")]
    public async Task<IActionResult> Start(string code)
    {
        var table = await FindTable(code);
        if (table == null) return View("NotFound");
        HttpContext.Session.SetTableCode(table.Code);
        HttpContext.Session.ResetAskedToLogin();   // quét QR mới -> được hỏi đăng nhập lại 1 lần
        return RedirectToAction(nameof(Menu), new { code });
    }

    [HttpGet("menu")]
    public async Task<IActionResult> Menu(string code, int? categoryId, string? q)
    {
        var table = await FindTable(code);
        if (table == null) return View("NotFound");
        HttpContext.Session.SetTableCode(table.Code);
        ViewBag.Open = await orders.GetOpenTableOrderAsync(table.Id);

        // Khách chưa đăng nhập: hỏi 1 lần xem có muốn đăng nhập để nhận ưu đãi không
        var askLogin = User.Identity?.IsAuthenticated != true && !HttpContext.Session.WasAskedToLogin();
        if (askLogin)
        {
            HttpContext.Session.SetAskedToLogin();
            ViewBag.Promos = await db.Vouchers.Where(v => v.IsActive && v.PointCost == 0 && v.EndAt >= DateTime.Today)
                .OrderByDescending(v => v.Value).Take(3).ToListAsync();
        }
        ViewBag.AskLogin = askLogin;

        await this.LoadMenuAsync(db, categoryId, q, table);
        return View();
    }

    // Khách gửi giỏ hàng cho bàn -> thêm vào đơn đang mở
    [HttpPost("submit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string code)
    {
        var table = await FindTable(code);
        if (table == null) return View("NotFound");
        var cart = HttpContext.Session.GetCart();
        if (cart.Count == 0) { TempData["Error"] = "Giỏ hàng đang trống."; return RedirectToAction(nameof(Menu), new { code }); }

        var (order, error) = await orders.AddToTableAsync(table, cart, User.GetUserId());
        if (error != null) { TempData["Error"] = error; return RedirectToAction("Index", "Cart"); }
        HttpContext.Session.ClearCart();
        TempData["Success"] = "Đã gửi món! Nhân viên sẽ mang ra bàn cho bạn ngay.";
        return RedirectToAction(nameof(Bill), new { code });
    }

    [HttpGet("bill")]
    public async Task<IActionResult> Bill(string code)
    {
        var table = await FindTable(code);
        if (table == null) return View("NotFound");
        ViewBag.Table = table;
        var order = await orders.GetOpenTableOrderAsync(table.Id);
        if (order == null)
        {
            // Không còn đơn mở: có thể vừa thanh toán xong
            ViewBag.LastPaid = await db.Orders.Include(o => o.Items)
                .Where(o => o.TableId == table.Id && o.Status == OrderStatus.Completed)
                .OrderByDescending(o => o.PaidAt).FirstOrDefaultAsync();
        }
        return View(order);
    }

    // SSE: hóa đơn bàn tự cập nhật khi nhân viên ra món / xác nhận thanh toán
    [HttpGet("stream")]
    public async Task Stream(string code)
    {
        var table = await FindTable(code);
        if (table == null) { Response.StatusCode = 404; return; }
        await Sse.StreamAsync(HttpContext, hub, "table:" + table.Id);
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(string code)
    {
        var table = await FindTable(code);
        if (table == null) return NotFound();
        var o = await orders.GetOpenTableOrderAsync(table.Id);
        return Json(new
        {
            table = table.Status.ToString(),
            order = o == null ? null : new { o.Id, o.Total, items = o.Items.Count, served = o.Items.Count(i => i.IsServed), status = o.Status.ToString() }
        });
    }

    [HttpPost("request-payment"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestPayment(string code, PaymentMethod method)
    {
        var table = await FindTable(code);
        var order = table == null ? null : await orders.GetOpenTableOrderAsync(table.Id);
        if (table == null || order == null) return RedirectToAction(nameof(Bill), new { code });
        if (order.Items.Any(i => !i.IsServed))
        {
            TempData["Error"] = "Vui lòng đợi ra đủ món rồi mới thanh toán.";
            return RedirectToAction(nameof(Bill), new { code });
        }
        order.Status = OrderStatus.WaitingPayment;
        order.PaymentMethod = method;
        order.UpdatedAt = DateTime.Now;
        table.Status = TableStatus.WaitingPayment;
        await db.SaveChangesAsync();
        TempData["Success"] = "Đã gọi thanh toán. Nhân viên sẽ đến bàn trong giây lát.";
        return RedirectToAction(nameof(Bill), new { code });
    }

    [HttpPost("voucher"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Voucher(string code, string? voucherCode)
    {
        var table = await FindTable(code);
        var order = table == null ? null : await orders.GetOpenTableOrderAsync(table.Id);
        if (table == null || order == null) return RedirectToAction(nameof(Bill), new { code });

        var uid = User.GetUserId();
        if (uid == null) { TempData["Error"] = "Vui lòng đăng nhập để dùng voucher."; return RedirectToAction(nameof(Bill), new { code }); }
        if (order.UserId == null) order.UserId = uid;
        if (order.UserId != uid) { TempData["Error"] = "Đơn này đang gắn với tài khoản khác."; return RedirectToAction(nameof(Bill), new { code }); }

        var err = await orders.ChangeVoucherAsync(order, voucherCode);
        if (err != null) TempData["Error"] = err;
        else TempData["Success"] = string.IsNullOrWhiteSpace(voucherCode) ? "Đã gỡ voucher." : "Đã áp dụng voucher.";
        return RedirectToAction(nameof(Bill), new { code });
    }

    // Đăng nhập sau khi gọi món: gắn bill vào tài khoản để tích điểm
    [HttpPost("claim"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Claim(string code)
    {
        var table = await FindTable(code);
        var order = table == null ? null : await orders.GetOpenTableOrderAsync(table.Id);
        var uid = User.GetUserId();
        if (order != null && uid != null && order.UserId == null)
        {
            order.UserId = uid;
            await db.SaveChangesAsync();
            TempData["Success"] = "Đã gắn bill vào tài khoản, bạn sẽ được tích điểm khi thanh toán.";
        }
        return RedirectToAction(nameof(Bill), new { code });
    }
}
