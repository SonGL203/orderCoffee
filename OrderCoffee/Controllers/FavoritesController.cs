using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;
using OrderCoffee.Services;

namespace OrderCoffee.Controllers;

[Authorize(Roles = Roles.Customer)]
public class FavoritesController(AppDbContext db) : Controller
{
    [HttpGet("/favorites")]
    public async Task<IActionResult> Index()
    {
        var uid = User.GetUserId();
        var list = await db.Favorites.Include(f => f.Product).ThenInclude(p => p!.Category)
            .Where(f => f.UserId == uid).OrderByDescending(f => f.Id).Select(f => f.Product!).ToListAsync();
        return View(list);
    }

    [HttpPost("/favorites/toggle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int productId)
    {
        var uid = User.GetUserId()!.Value;
        if (!await db.Products.AnyAsync(p => p.Id == productId)) return NotFound();
        var f = await db.Favorites.FirstOrDefaultAsync(x => x.UserId == uid && x.ProductId == productId);
        if (f == null) db.Favorites.Add(new FavoriteProduct { UserId = uid, ProductId = productId });
        else db.Favorites.Remove(f);
        await db.SaveChangesAsync();
        return Json(new { fav = f == null });
    }
}
