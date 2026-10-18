using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Services;

namespace OrderCoffee.Controllers;

// Giỏ hàng dùng chung cho order online và order tại bàn
public class CartController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var cart = HttpContext.Session.GetCart();
        var code = HttpContext.Session.GetTableCode();
        if (code != null) ViewBag.Table = await db.Tables.FirstOrDefaultAsync(t => t.Code == code);
        return View(cart);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, int quantity = 1, string? note = null, string? returnUrl = null)
    {
        var p = await db.Products.FindAsync(productId);
        if (p == null || !p.IsAvailable)
            return IsAjax() ? BadRequest(new { error = "Món hiện đã hết." }) : RedirectBack(returnUrl);

        var cart = HttpContext.Session.GetCart();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var line = cart.FirstOrDefault(c => c.ProductId == productId && c.Note == note);
        if (line == null) cart.Add(new CartItem { ProductId = p.Id, Name = p.Name, Price = p.Price, Quantity = Math.Clamp(quantity, 1, 50), Note = note });
        else line.Quantity = Math.Min(50, line.Quantity + Math.Max(1, quantity));
        HttpContext.Session.SaveCart(cart);

        if (IsAjax()) return Json(new { count = cart.Sum(c => c.Quantity), total = cart.Sum(c => c.LineTotal), name = p.Name });
        TempData["Success"] = $"Đã thêm \"{p.Name}\" vào giỏ.";
        return RedirectBack(returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Change(int index, int delta)
    {
        var cart = HttpContext.Session.GetCart();
        if (index >= 0 && index < cart.Count)
        {
            cart[index].Quantity += delta;
            if (cart[index].Quantity <= 0) cart.RemoveAt(index);
            else cart[index].Quantity = Math.Min(50, cart[index].Quantity);
            HttpContext.Session.SaveCart(cart);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Clear()
    {
        HttpContext.Session.ClearCart();
        return RedirectToAction(nameof(Index));
    }

    private bool IsAjax() => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    private IActionResult RedirectBack(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Menu", "Home");
}
