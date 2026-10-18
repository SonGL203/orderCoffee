using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;
using OrderCoffee.Services;

namespace OrderCoffee.Controllers;

public class HomeController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        // Vào trang chủ = chế độ online: quên bàn đang gắn
        HttpContext.Session.ClearTableCode();
        ViewBag.Featured = await db.Products.Where(p => p.IsAvailable).OrderBy(p => p.Id).Take(4).ToListAsync();
        ViewBag.Vouchers = await db.Vouchers.Where(v => v.IsActive && v.PointCost == 0 && v.EndAt >= DateTime.Today).ToListAsync();
        return View();
    }

    [HttpGet("/menu")]
    public async Task<IActionResult> Menu(int? categoryId, string? q)
    {
        HttpContext.Session.ClearTableCode();
        await this.LoadMenuAsync(db, categoryId, q, null);
        return View();
    }

    public IActionResult Error() => View();
}

public static class MenuExtensions
{
    // Nạp dữ liệu thực đơn vào ViewBag (dùng chung cho menu online và menu tại bàn)
    public static async Task LoadMenuAsync(this Controller c, AppDbContext db, int? categoryId, string? q, CafeTable? table)
    {
        var products = db.Products.Include(p => p.Category).AsQueryable();
        if (categoryId.HasValue) products = products.Where(p => p.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(q)) products = products.Where(p => EF.Functions.ILike(p.Name, "%" + q.Trim() + "%"));
        c.ViewBag.Categories = await db.Categories.OrderBy(x => x.SortOrder).ToListAsync();
        c.ViewBag.Products = await products.OrderBy(p => p.Category!.SortOrder).ThenBy(p => p.Name).ToListAsync();
        c.ViewBag.CategoryId = categoryId;
        c.ViewBag.Q = q;
        c.ViewBag.Table = table;
    }
}
