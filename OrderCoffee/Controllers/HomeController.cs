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
        // Món đề xuất: điểm sao cao nhất (nhiều lượt đánh giá hơn xếp trước); món chưa có đánh giá xếp sau cùng
        var top = await db.Reviews.GroupBy(r => r.ProductId)
            .Select(g => new { Id = g.Key, Avg = g.Average(r => r.Rating), Count = g.Count() })
            .OrderByDescending(x => x.Avg).ThenByDescending(x => x.Count).Take(4).ToListAsync();
        var ids = top.Select(x => x.Id).ToList();
        var rated = (await db.Products.Where(p => p.IsAvailable && ids.Contains(p.Id)).ToListAsync())
            .OrderBy(p => ids.IndexOf(p.Id)).ToList();
        ViewBag.Featured = rated.Concat(await db.Products.Where(p => p.IsAvailable && !ids.Contains(p.Id))
            .OrderBy(p => p.Id).Take(4 - rated.Count).ToListAsync()).ToList();
        ViewBag.Ratings = top.ToDictionary(x => x.Id, x => (Avg: x.Avg, Count: x.Count));
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
        c.ViewBag.Ratings = (await db.Reviews.GroupBy(r => r.ProductId)
            .Select(g => new { g.Key, Avg = g.Average(r => r.Rating), Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => (Avg: x.Avg, Count: x.Count));
        var uid = c.User.IsInRole(Roles.Customer) ? c.User.GetUserId() : null;
        c.ViewBag.FavIds = uid == null ? new HashSet<int>()
            : (await db.Favorites.Where(f => f.UserId == uid).Select(f => f.ProductId).ToListAsync()).ToHashSet();
        c.ViewBag.CanFav = uid != null;
    }
}
