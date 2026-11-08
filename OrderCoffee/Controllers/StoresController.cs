using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;
using OrderCoffee.Services;

namespace OrderCoffee.Controllers;

// Công khai: khách đăng nhập hay chưa đều xem được
public class StoresController(AppDbContext db) : Controller
{
    [HttpGet("/stores")]
    public async Task<IActionResult> Index() =>
        View(await db.Stores.OrderBy(s => s.City).ThenBy(s => s.Address).ToListAsync());

    // Admin bấm để tìm lại chi nhánh trên bản đồ và cập nhật database
    [HttpPost("/stores/sync"), Authorize(Roles = Roles.Admin), ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync([FromServices] StoreSyncService sync, CancellationToken ct)
    {
        var (added, updated) = await sync.SyncAsync(ct);
        TempData["Success"] = $"Đã đồng bộ cửa hàng từ OpenStreetMap: thêm {added}, cập nhật {updated}.";
        return RedirectToAction(nameof(Index));
    }
}
