using Microsoft.EntityFrameworkCore;
using OrderCoffee.Data;
using OrderCoffee.Models;

namespace OrderCoffee.Services;

public record VoucherCheck(Voucher? Voucher, UserVoucher? UserVoucher, int Discount, string? Error)
{
    public bool Ok => Error == null && Voucher != null;
}

public class PromotionService(AppDbContext db)
{
    // 10.000đ thanh toán = 1 điểm
    public const int MoneyPerPoint = 10000;

    public static int CalcDiscount(Voucher v, int subTotal)
    {
        var d = v.Type == VoucherType.Percent ? subTotal * v.Value / 100 : v.Value;
        if (v.MaxDiscount.HasValue) d = Math.Min(d, v.MaxDiscount.Value);
        return Math.Min(d, subTotal);
    }

    /// <summary>Kiểm tra mã voucher cho một đơn. Không thay đổi dữ liệu.</summary>
    public async Task<VoucherCheck> CheckAsync(string? code, int? userId, int subTotal, int? excludeOrderId = null)
    {
        if (string.IsNullOrWhiteSpace(code)) return new(null, null, 0, null);
        if (userId == null) return new(null, null, 0, "Vui lòng đăng nhập để dùng voucher.");

        code = code.Trim().ToUpperInvariant();
        var v = await db.Vouchers.FirstOrDefaultAsync(x => x.Code == code);
        var now = DateTime.Now;
        if (v == null || !v.IsActive) return new(null, null, 0, "Mã voucher không tồn tại.");
        if (now < v.StartAt || now > v.EndAt.Date.AddDays(1)) return new(null, null, 0, "Voucher chưa bắt đầu hoặc đã hết hạn.");
        if (subTotal < v.MinOrder) return new(null, null, 0, $"Đơn tối thiểu {Fmt.Money(v.MinOrder)} để dùng mã này.");

        UserVoucher? uv = null;
        if (v.PointCost > 0)
        {
            uv = await db.UserVouchers.FirstOrDefaultAsync(x => x.UserId == userId && x.VoucherId == v.Id && !x.IsUsed);
            if (uv == null) return new(null, null, 0, "Bạn chưa đổi điểm lấy voucher này (xem mục Đổi điểm).");
        }
        else
        {
            if (v.UsageLimit.HasValue && v.UsedCount >= v.UsageLimit) return new(null, null, 0, "Voucher đã hết lượt sử dụng.");
            if (v.PerUserLimit.HasValue)
            {
                var used = await db.Orders.CountAsync(o => o.UserId == userId && o.VoucherId == v.Id
                    && o.Status != OrderStatus.Cancelled && o.Id != excludeOrderId);
                if (used >= v.PerUserLimit) return new(null, null, 0, "Bạn đã dùng hết lượt của voucher này.");
            }
        }
        return new(v, uv, CalcDiscount(v, subTotal), null);
    }

    /// <summary>Gắn voucher vào đơn và giữ lượt dùng (gọi trước SaveChanges).</summary>
    public void Apply(Order order, VoucherCheck chk)
    {
        if (!chk.Ok) return;
        order.VoucherId = chk.Voucher!.Id;
        order.UserVoucherId = chk.UserVoucher?.Id;
        chk.Voucher.UsedCount++;
        if (chk.UserVoucher != null) chk.UserVoucher.IsUsed = true;
        Recalc(order, chk.Voucher);
    }

    /// <summary>Trả lại lượt voucher khi hủy đơn hoặc đổi voucher khác.</summary>
    public async Task ReleaseAsync(Order order)
    {
        if (order.VoucherId == null) return;
        var v = await db.Vouchers.FindAsync(order.VoucherId);
        if (v != null && v.UsedCount > 0) v.UsedCount--;
        if (order.UserVoucherId != null)
        {
            var uv = await db.UserVouchers.FindAsync(order.UserVoucherId);
            if (uv != null) { uv.IsUsed = false; uv.UsedOrderId = null; }
        }
        order.VoucherId = null;
        order.UserVoucherId = null;
        order.Voucher = null;
        Recalc(order, null);
    }

    public static void Recalc(Order order, Voucher? voucher)
    {
        order.SubTotal = order.Items.Sum(i => i.LineTotal);
        order.Discount = voucher == null ? 0 : CalcDiscount(voucher, order.SubTotal);
        order.Total = order.SubTotal - order.Discount;
    }

    public async Task AwardPointsAsync(Order order)
    {
        if (order.UserId == null || order.PointsEarned > 0) return;
        var points = order.Total / MoneyPerPoint;
        if (points <= 0) return;
        var user = await db.Users.FindAsync(order.UserId);
        if (user == null) return;
        user.Points += points;
        order.PointsEarned = points;
        db.PointTransactions.Add(new PointTransaction { UserId = user.Id, Amount = points, OrderId = order.Id, Reason = $"Tích điểm đơn #{order.Id}" });
        if (order.UserVoucherId != null)
        {
            var uv = await db.UserVouchers.FindAsync(order.UserVoucherId);
            if (uv != null) uv.UsedOrderId = order.Id;
        }
    }

    public async Task<string?> RedeemAsync(int userId, int voucherId)
    {
        var user = await db.Users.FindAsync(userId);
        var v = await db.Vouchers.FindAsync(voucherId);
        if (user == null || v == null || !v.IsActive || v.PointCost <= 0 || DateTime.Now > v.EndAt.Date.AddDays(1))
            return "Voucher không hợp lệ.";
        if (user.Points < v.PointCost) return "Bạn không đủ điểm.";
        if (v.UsageLimit.HasValue && v.UsedCount >= v.UsageLimit) return "Voucher đã hết lượt.";

        user.Points -= v.PointCost;
        db.UserVouchers.Add(new UserVoucher { UserId = userId, VoucherId = v.Id });
        db.PointTransactions.Add(new PointTransaction { UserId = userId, Amount = -v.PointCost, Reason = $"Đổi voucher {v.Code}" });
        await db.SaveChangesAsync();
        return null;
    }
}
