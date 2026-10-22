using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;
using OrderCoffee.Services;

namespace OrderCoffee.Controllers;

public class SepayWebhookModel
{
    public long Id { get; set; }
    public string? Gateway { get; set; }
    public string? TransactionDate { get; set; }
    public string? AccountNumber { get; set; }
    public string? Code { get; set; }
    public string? Content { get; set; }
    public string? TransferType { get; set; } // "in"
    public decimal TransferAmount { get; set; }
    public decimal Accumulated { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Description { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class PaymentWebhookController(AppDbContext db, PromotionService promo, IConfiguration config, EventHub hub, ILogger<PaymentWebhookController> logger) : ControllerBase
{
    [HttpPost("sepay")]
    public async Task<IActionResult> HandleSepayWebhook([FromBody] SepayWebhookModel payload)
    {
        // 1. Kiểm tra xác thực qua Authorization header (SePay gửi format: "Apikey {API_KEY}")
        var configuredApiKey = config["SePay:ApiKey"];
        if (!string.IsNullOrWhiteSpace(configuredApiKey))
        {
            var authHeader = Request.Headers.Authorization.ToString();
            var isValid = authHeader.Equals($"Apikey {configuredApiKey}", StringComparison.OrdinalIgnoreCase)
                          || authHeader.Equals(configuredApiKey, StringComparison.OrdinalIgnoreCase);

            if (!isValid)
            {
                logger.LogWarning("SePay webhook rejected: Invalid API Key. Header: {Header}", authHeader);
                return Unauthorized(new { success = false, message = "Invalid API Key" });
            }
        }

        // 2. Chỉ xử lý tiền vào (in)
        if (!string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase) && payload.TransferAmount <= 0)
        {
            return Ok(new { success = true, message = "Ignored non-incoming transaction" });
        }

        var content = payload.Content ?? "";
        logger.LogInformation("Nhận webhook SePay: Số tiền {Amount}, Nội dung: {Content}", payload.TransferAmount, content);

        // 3. Tìm mã đơn hàng từ nội dung: hỗ trợ định dạng "DON 123", "DON123", "DH123", "ORDER123" hoặc mã code Order.Code
        var match = Regex.Match(content, @"(?:DON|DH|ORDER)\s*([0-9]+)", RegexOptions.IgnoreCase);
        Order? order = null;

        if (match.Success && int.TryParse(match.Groups[1].Value, out var orderId))
        {
            order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId);
        }

        // Nếu không khớp ID, thử tìm theo mã Order.Code có trong nội dung (ví dụ 8 ký tự)
        if (order == null)
        {
            var matchCode = Regex.Match(content, @"([A-Z0-9]{6,12})", RegexOptions.IgnoreCase);
            if (matchCode.Success)
            {
                var candidateCode = matchCode.Groups[1].Value;
                order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Code == candidateCode);
            }
        }

        if (order == null)
        {
            logger.LogWarning("Không tìm thấy đơn hàng tương ứng với nội dung: {Content}", content);
            return Ok(new { success = false, message = "Order not found" });
        }

        // 4. Nếu đơn đã thanh toán hoặc đã hủy thì bỏ qua
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            return Ok(new { success = true, message = "Order already paid" });
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return Ok(new { success = false, message = "Order was cancelled" });
        }

        // 5. Kiểm tra số tiền chuyển có đủ tiền đơn hàng không
        if (payload.TransferAmount < order.Total)
        {
            logger.LogWarning("Đơn #{Id}: Số tiền chuyển {TransAmount} nhỏ hơn tổng tiền {Total}", order.Id, payload.TransferAmount, order.Total);
            return Ok(new { success = false, message = "Transfer amount is less than order total" });
        }

        // 6. Cập nhật trạng thái đơn hàng sang Đã thanh toán
        order.PaymentStatus = PaymentStatus.Paid;
        order.PaidAt = DateTime.Now;
        order.PaymentMethod = PaymentMethod.BankTransfer;
        
        // Nếu là đơn online đang ở Pending, chuyển sang Confirmed (Đã xác nhận)
        if (order.Type == OrderType.Online && order.Status == OrderStatus.Pending)
        {
            order.Status = OrderStatus.Confirmed;
        }

        order.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();

        // 7. Bắn sự kiện realtime cập nhật giao diện khách và nhân viên
        hub.Publish($"order:{order.Code}", "paid");
        hub.Publish("staff", $"order:{order.Id}:paid");
        if (order.TableId != null)
        {
            hub.Publish($"table:{order.TableId}", "paid");
        }

        logger.LogInformation("Thanh toán thành công cho đơn #{Id} qua SePay!", order.Id);
        return Ok(new { success = true, message = "Payment confirmed successfully" });
    }
}
