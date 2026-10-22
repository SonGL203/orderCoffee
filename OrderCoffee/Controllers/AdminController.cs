using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;
using OrderCoffee.Services;
using QRCoder;

namespace OrderCoffee.Controllers;

[Authorize(Roles = Roles.Admin)]
public class AdminController(AppDbContext db, IConfiguration config) : Controller
{
    // ---------- Báo cáo ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to)
    {
        var f = (from ?? DateTime.Today.AddDays(-6)).Date;
        var t = (to ?? DateTime.Today).Date;
        var end = t.AddDays(1);
        var done = await db.Orders.Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Completed && o.PaidAt >= f && o.PaidAt < end).ToListAsync();

        ViewBag.From = f;
        ViewBag.To = t;
        ViewBag.Revenue = done.Sum(o => o.Total);
        ViewBag.OrderCount = done.Count;
        ViewBag.OnlineCount = done.Count(o => o.Type == OrderType.Online);
        ViewBag.DineInCount = done.Count(o => o.Type == OrderType.DineIn);
        ViewBag.DiscountTotal = done.Sum(o => o.Discount);
        ViewBag.Daily = Enumerable.Range(0, (t - f).Days + 1).Select(i => f.AddDays(i))
            .Select(d => (Day: d, Revenue: done.Where(o => o.PaidAt!.Value.Date == d).Sum(o => o.Total), Orders: done.Count(o => o.PaidAt!.Value.Date == d))).ToList();
        ViewBag.TopProducts = done.SelectMany(o => o.Items).GroupBy(i => i.ProductName)
            .Select(g => (Name: g.Key, Qty: g.Sum(i => i.Quantity), Revenue: g.Sum(i => i.LineTotal)))
            .OrderByDescending(x => x.Qty).Take(10).ToList();
        ViewBag.Customers = await db.Users.CountAsync(u => u.Role == Roles.Customer);
        return View();
    }

    // ---------- Danh mục ----------
    public async Task<IActionResult> Categories() =>
        View(await db.Categories.Include(c => c.Products).OrderBy(c => c.SortOrder).ToListAsync());

    public async Task<IActionResult> CategoryEdit(int? id) =>
        View(id == null ? new Category { SortOrder = 99 } : await db.Categories.FindAsync(id));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CategoryEdit(Category m)
    {
        ModelState.Remove(nameof(m.Products));
        if (!ModelState.IsValid) return View(m);
        if (m.Id == 0) db.Categories.Add(m); else db.Categories.Update(m);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CategoryDelete(int id)
    {
        var c = await db.Categories.Include(x => x.Products).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        if (c.Products.Count > 0) TempData["Error"] = "Danh mục còn món, hãy chuyển hoặc xóa món trước.";
        else { db.Categories.Remove(c); await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Categories));
    }

    // ---------- Món ----------
    public async Task<IActionResult> Products() =>
        View(await db.Products.Include(p => p.Category).OrderBy(p => p.Category!.SortOrder).ThenBy(p => p.Name).ToListAsync());

    public async Task<IActionResult> ProductEdit(int? id)
    {
        ViewBag.Categories = await db.Categories.OrderBy(c => c.SortOrder).ToListAsync();
        return View(id == null ? new Product() : await db.Products.FindAsync(id));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ProductEdit(Product m)
    {
        ModelState.Remove(nameof(m.Category));
        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await db.Categories.OrderBy(c => c.SortOrder).ToListAsync();
            return View(m);
        }
        if (m.Id == 0) db.Products.Add(m); else db.Products.Update(m);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Products));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ProductToggle(int id)
    {
        var p = await db.Products.FindAsync(id);
        if (p == null) return NotFound();
        p.IsAvailable = !p.IsAvailable;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Products));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ProductDelete(int id)
    {
        var p = await db.Products.FindAsync(id);
        if (p == null) return NotFound();
        db.Products.Remove(p); // OrderItem lưu tên/giá riêng nên bill cũ không ảnh hưởng
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Products));
    }

    // ---------- Bàn + QR ----------
    public async Task<IActionResult> Tables(string? mode)
    {
        ViewBag.Mode = mode;
        ViewBag.BaseUrl = BaseUrl(mode);
        ViewBag.IsFixed = config["Shop:PublicBaseUrl"] is { Length: > 0 };
        ViewBag.HostName = LanAddress.HostName();
        ViewBag.Candidates = LanAddress.Candidates();
        return View(await db.Tables.OrderBy(t => t.Id).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TableAdd(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return RedirectToAction(nameof(Tables));
        db.Tables.Add(new CafeTable { Name = name.Trim(), Code = SeedData.NewTableCode() });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Tables));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TableDelete(int id)
    {
        var t = await db.Tables.FindAsync(id);
        if (t == null) return NotFound();
        if (t.Status != TableStatus.Available) TempData["Error"] = "Bàn đang có khách, không thể xóa.";
        else { db.Tables.Remove(t); await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Tables));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TableRegenCode(int id)
    {
        var t = await db.Tables.FindAsync(id);
        if (t == null) return NotFound();
        t.Code = SeedData.NewTableCode();
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Tables));
    }

    public async Task<IActionResult> TableQr(int id, string? mode)
    {
        var t = await db.Tables.FindAsync(id);
        if (t == null) return NotFound();
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(TableUrl(t, mode), QRCodeGenerator.ECCLevel.M);
        return File(new PngByteQRCode(data).GetGraphic(12), "image/png");
    }

    // Trang in QR dán lên bàn
    public async Task<IActionResult> TablePrint(string? mode)
    {
        ViewBag.Mode = mode;
        ViewBag.Urls = (await db.Tables.ToListAsync()).ToDictionary(t => t.Id, t => TableUrl(t, mode));
        return View(await db.Tables.OrderBy(t => t.Id).ToListAsync());
    }

    // Địa chỉ gốc trong QR, theo thứ tự ưu tiên:
    //  1) Shop:PublicBaseUrl nếu có (domain / tunnel cố định)
    //  2) mode=host -> tên máy (songiang.local), không đổi khi đổi Wi-Fi
    //  3) mặc định -> IP LAN hiện tại của máy, tự nhận mỗi lần mở trang (đổi Wi-Fi chỉ cần tải lại)
    private string BaseUrl(string? mode)
    {
        if (config["Shop:PublicBaseUrl"] is { Length: > 0 } fixedUrl) return fixedUrl.TrimEnd('/');
        var port = config.GetValue("Shop:LanPort", 5190);
        var host = mode == "host" ? LanAddress.HostName() : LanAddress.CurrentIp() ?? Request.Host.Host;
        return $"http://{host}:{port}";
    }

    private string TableUrl(CafeTable t, string? mode) => $"{BaseUrl(mode)}/t/{t.Code}";

    // ---------- Voucher ----------
    public async Task<IActionResult> Vouchers() => View(await db.Vouchers.OrderByDescending(v => v.Id).ToListAsync());

    public async Task<IActionResult> VoucherEdit(int? id) =>
        View(id == null ? new Voucher() : await db.Vouchers.FindAsync(id));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VoucherEdit(Voucher m)
    {
        m.Code = (m.Code ?? "").Trim().ToUpperInvariant();
        if (m.Type == VoucherType.Percent && m.Value > 100) ModelState.AddModelError(nameof(m.Value), "Phần trăm tối đa 100");
        if (m.EndAt < m.StartAt) ModelState.AddModelError(nameof(m.EndAt), "Ngày kết thúc phải sau ngày bắt đầu");
        if (await db.Vouchers.AnyAsync(v => v.Code == m.Code && v.Id != m.Id)) ModelState.AddModelError(nameof(m.Code), "Mã đã tồn tại");
        if (!ModelState.IsValid) return View(m);
        if (m.Id == 0) db.Vouchers.Add(m);
        else
        {
            var old = await db.Vouchers.AsNoTracking().FirstAsync(v => v.Id == m.Id);
            m.UsedCount = old.UsedCount; // không cho sửa số lượt đã dùng
            db.Vouchers.Update(m);
        }
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Vouchers));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VoucherToggle(int id)
    {
        var v = await db.Vouchers.FindAsync(id);
        if (v == null) return NotFound();
        v.IsActive = !v.IsActive;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Vouchers));
    }

    // ---------- Tài khoản ----------
    public async Task<IActionResult> Users() => View(await db.Users.OrderBy(u => u.Role).ThenBy(u => u.Id).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UserRole(int id, string role)
    {
        var u = await db.Users.FindAsync(id);
        if (u == null) return NotFound();
        if (u.Id == User.GetUserId()) TempData["Error"] = "Không thể tự đổi quyền của chính mình.";
        else if (role is Roles.Admin or Roles.Staff or Roles.Customer) { u.Role = role; await db.SaveChangesAsync(); TempData["Success"] = $"Đã đổi quyền {u.FullName} thành {role}. Có hiệu lực ở lần đăng nhập sau."; }
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UserLock(int id)
    {
        var u = await db.Users.FindAsync(id);
        if (u == null) return NotFound();
        if (u.Id == User.GetUserId()) TempData["Error"] = "Không thể tự khóa chính mình.";
        else { u.IsLocked = !u.IsLocked; await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Users));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> StaffCreate(string fullName, string phone, string password, string role = Roles.Staff)
    {
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone) || password?.Length < 6)
            TempData["Error"] = "Nhập đủ họ tên, số điện thoại và mật khẩu (>= 6 ký tự).";
        else if (await db.Users.AnyAsync(u => u.Phone == phone)) TempData["Error"] = "Số điện thoại đã tồn tại.";
        else
        {
            var u = new User { FullName = fullName.Trim(), Phone = phone.Trim(), Role = role == Roles.Admin ? Roles.Admin : Roles.Staff };
            u.PasswordHash = new PasswordHasher<User>().HashPassword(u, password!);
            db.Users.Add(u);
            await db.SaveChangesAsync();
            TempData["Success"] = "Đã tạo tài khoản nhân viên.";
        }
        return RedirectToAction(nameof(Users));
    }
}
