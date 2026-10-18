using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;
using OrderCoffee.Services;

namespace OrderCoffee.Controllers;

public class AccountController(AppDbContext db, PromotionService promo) : Controller
{
    private readonly PasswordHasher<User> _hasher = new();

    private async Task SignInAsync(User u, bool remember)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.FullName),
            new(ClaimTypes.Role, u.Role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(principal, new AuthenticationProperties { IsPersistent = remember });
    }

    private IActionResult Back(string? returnUrl, User u)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        // Nếu đang ngồi ở bàn thì quay lại menu bàn
        if (u.Role == Roles.Customer && HttpContext.Session.GetTableCode() is { } code) return Redirect($"/t/{code}");
        return u.Role switch
        {
            Roles.Admin => RedirectToAction("Index", "Admin"),
            Roles.Staff => RedirectToAction("Index", "Staff"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl) => View(new LoginVm { ReturnUrl = returnUrl });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var key = vm.Login.Trim().ToLower();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == key || (u.Email != null && u.Email.ToLower() == key));
        if (user == null || _hasher.VerifyHashedPassword(user, user.PasswordHash, vm.Password) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("", "Sai tài khoản hoặc mật khẩu.");
            return View(vm);
        }
        if (user.IsLocked)
        {
            ModelState.AddModelError("", "Tài khoản đã bị khóa, vui lòng liên hệ quán.");
            return View(vm);
        }
        await SignInAsync(user, vm.RememberMe);
        return Back(vm.ReturnUrl, user);
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl) => View(new RegisterVm { ReturnUrl = returnUrl });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterVm vm)
    {
        if (!string.IsNullOrWhiteSpace(vm.Email)) vm.Email = vm.Email.Trim().ToLower(); else vm.Email = null;
        if (await db.Users.AnyAsync(u => u.Phone == vm.Phone)) ModelState.AddModelError(nameof(vm.Phone), "Số điện thoại đã được đăng ký.");
        if (vm.Email != null && await db.Users.AnyAsync(u => u.Email == vm.Email)) ModelState.AddModelError(nameof(vm.Email), "Email đã được đăng ký.");
        if (!ModelState.IsValid) return View(vm);

        var user = new User { FullName = vm.FullName.Trim(), Phone = vm.Phone, Email = vm.Email, Role = Roles.Customer };
        user.PasswordHash = _hasher.HashPassword(user, vm.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await SignInAsync(user, false);
        TempData["Success"] = "Đăng ký thành công! Chúc bạn có trải nghiệm vui vẻ.";
        return Back(vm.ReturnUrl, user);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        HttpContext.Session.Clear();
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied() => View();

    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var uid = User.GetUserId()!.Value;
        ViewBag.User = await db.Users.FindAsync(uid);
        ViewBag.History = await db.PointTransactions.Where(p => p.UserId == uid).OrderByDescending(p => p.Id).Take(30).ToListAsync();
        ViewBag.Addresses = await db.UserAddresses.Where(a => a.UserId == uid).OrderByDescending(a => a.LastUsedAt).ToListAsync();
        ViewBag.Wallet = await db.UserVouchers.Include(x => x.Voucher).Where(x => x.UserId == uid && !x.IsUsed)
            .OrderByDescending(x => x.Id).ToListAsync();
        ViewBag.Redeemable = await db.Vouchers.Where(v => v.IsActive && v.PointCost > 0 && v.EndAt >= DateTime.Today)
            .OrderBy(v => v.PointCost).ToListAsync();
        ViewBag.PublicVouchers = await db.Vouchers.Where(v => v.IsActive && v.PointCost == 0 && v.EndAt >= DateTime.Today)
            .OrderBy(v => v.EndAt).ToListAsync();
        return View();
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Redeem(int voucherId)
    {
        var err = await promo.RedeemAsync(User.GetUserId()!.Value, voucherId);
        if (err == null) TempData["Success"] = "Đổi điểm thành công! Voucher đã nằm trong ví của bạn.";
        else TempData["Error"] = err;
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAddress(string? contactName, string? phone, string? address)
    {
        var uid = User.GetUserId()!.Value;
        phone = phone?.Trim(); address = address?.Trim();
        if (string.IsNullOrEmpty(phone) || !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^0\d{9}$"))
            TempData["Error"] = "Số điện thoại gồm 10 số, bắt đầu bằng 0.";
        else if (string.IsNullOrEmpty(address) || address.Length > 200)
            TempData["Error"] = "Nhập địa chỉ (tối đa 200 ký tự).";
        else if (await db.UserAddresses.AnyAsync(a => a.UserId == uid && a.Phone == phone && a.Address == address))
            TempData["Error"] = "Địa chỉ này đã có trong sổ.";
        else
        {
            db.UserAddresses.Add(new UserAddress { UserId = uid, Phone = phone, Address = address, ContactName = string.IsNullOrWhiteSpace(contactName) ? null : contactName.Trim() });
            await db.SaveChangesAsync();
            TempData["Success"] = "Đã lưu địa chỉ.";
        }
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAddress(int id)
    {
        var uid = User.GetUserId()!.Value;
        var a = await db.UserAddresses.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);   // chỉ xóa địa chỉ của chính mình
        if (a != null) { db.UserAddresses.Remove(a); await db.SaveChangesAsync(); TempData["Success"] = "Đã xóa địa chỉ."; }
        return RedirectToAction(nameof(Profile));
    }
}
