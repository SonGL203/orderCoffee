using System.ComponentModel.DataAnnotations;

namespace OrderCoffee.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Customer = "Customer";
    public const string AdminOrStaff = "Admin,Staff";
}

public enum OrderType { Online, DineIn }

public enum OrderStatus
{
    // Online
    Pending, Confirmed, Preparing, Ready, Delivering,
    // Tại bàn
    Open, WaitingPayment,
    // Chung
    Completed, Cancelled
}

public enum TableStatus { Available, Occupied, WaitingPayment }
public enum DeliveryType { Pickup, Delivery }
public enum PaymentMethod { Cash, BankTransfer }
public enum PaymentStatus { Unpaid, Paid }
public enum VoucherType { Percent, Fixed }

public class User
{
    public int Id { get; set; }
    [MaxLength(100)] public string FullName { get; set; } = "";
    [MaxLength(20)] public string Phone { get; set; } = "";
    [MaxLength(100)] public string? Email { get; set; }
    public string PasswordHash { get; set; } = "";
    [MaxLength(20)] public string Role { get; set; } = Roles.Customer;
    public int Points { get; set; }
    public bool IsLocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class Category
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Name { get; set; } = "";
    [MaxLength(10)] public string Icon { get; set; } = "☕";
    public int SortOrder { get; set; }
    public List<Product> Products { get; set; } = new();
}

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(300)] public string? Description { get; set; }
    [Range(0, 10_000_000)] public int Price { get; set; }
    [MaxLength(300)] public string? ImageUrl { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public class CafeTable
{
    public int Id { get; set; }
    [Required, MaxLength(30)] public string Name { get; set; } = "";
    [MaxLength(20)] public string Code { get; set; } = "";
    public TableStatus Status { get; set; } = TableStatus.Available;
}

public class Order
{
    public int Id { get; set; }
    // Mã tra cứu: khách vãng lai theo dõi đơn bằng link chứa mã này
    [MaxLength(20)] public string Code { get; set; } =
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
    public OrderType Type { get; set; }
    public OrderStatus Status { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
    public int? TableId { get; set; }
    public CafeTable? Table { get; set; }

    public DeliveryType? DeliveryType { get; set; }
    [MaxLength(200)] public string? Address { get; set; }
    [MaxLength(100)] public string? CustomerName { get; set; }
    [MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(300)] public string? Note { get; set; }

    public int SubTotal { get; set; }
    public int Discount { get; set; }
    public int Total { get; set; }
    public int? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public int? UserVoucherId { get; set; }
    public int PointsEarned { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public DateTime? PaidAt { get; set; }
    public int? HandledByStaffId { get; set; }
    [MaxLength(200)] public string? CancelReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    // Lưu lại tên + giá lúc đặt để đổi menu không làm sai bill cũ
    [MaxLength(100)] public string ProductName { get; set; } = "";
    public int UnitPrice { get; set; }
    public int Quantity { get; set; }
    [MaxLength(200)] public string? Note { get; set; }
    public int Batch { get; set; } = 1;
    public bool IsServed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int LineTotal => UnitPrice * Quantity;
}

public class Voucher
{
    public int Id { get; set; }
    [Required, MaxLength(30)] public string Code { get; set; } = "";
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public VoucherType Type { get; set; }
    [Range(1, 10_000_000)] public int Value { get; set; }
    public int? MaxDiscount { get; set; }
    public int MinOrder { get; set; }
    public DateTime StartAt { get; set; } = DateTime.Today;
    public DateTime EndAt { get; set; } = DateTime.Today.AddMonths(1);
    public int? UsageLimit { get; set; }
    public int UsedCount { get; set; }
    public int? PerUserLimit { get; set; }
    // 0 = mã công khai; > 0 = phải đổi điểm để nhận vào ví
    public int PointCost { get; set; }
    public bool IsActive { get; set; } = true;

    public string DisplayValue => Type == VoucherType.Percent
        ? $"Giảm {Value}%" + (MaxDiscount.HasValue ? $" (tối đa {Fmt.Money(MaxDiscount.Value)})" : "")
        : $"Giảm {Fmt.Money(Value)}";
}

public class UserVoucher
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public bool IsUsed { get; set; }
    public int? UsedOrderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

// Sổ địa chỉ giao hàng của khách đã đăng ký: tự lưu mỗi khi đặt đơn, dùng lại ở lần sau
public class UserAddress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    [MaxLength(100)] public string? ContactName { get; set; }
    [MaxLength(20)] public string Phone { get; set; } = "";
    [MaxLength(200)] public string Address { get; set; } = "";
    public int UseCount { get; set; }
    public DateTime LastUsedAt { get; set; } = DateTime.Now;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class PointTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Amount { get; set; }
    [MaxLength(200)] public string Reason { get; set; } = "";
    public int? OrderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public static class Fmt
{
    public static string Money(int v) => v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + "đ";

    public static string Status(OrderStatus s) => s switch
    {
        OrderStatus.Pending => "Chờ xác nhận",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Preparing => "Đang pha chế",
        OrderStatus.Ready => "Sẵn sàng lấy",
        OrderStatus.Delivering => "Đang giao",
        OrderStatus.Open => "Đang phục vụ",
        OrderStatus.WaitingPayment => "Chờ thanh toán",
        OrderStatus.Completed => "Hoàn thành",
        OrderStatus.Cancelled => "Đã hủy",
        _ => s.ToString()
    };

    public static string StatusColor(OrderStatus s) => s switch
    {
        OrderStatus.Pending or OrderStatus.WaitingPayment => "warning",
        OrderStatus.Confirmed or OrderStatus.Open => "info",
        OrderStatus.Preparing => "primary",
        OrderStatus.Ready or OrderStatus.Delivering => "success",
        OrderStatus.Completed => "secondary",
        OrderStatus.Cancelled => "danger",
        _ => "light"
    };

    public static string Table(TableStatus s) => s switch
    {
        TableStatus.Available => "Trống",
        TableStatus.Occupied => "Có khách",
        TableStatus.WaitingPayment => "Chờ thanh toán",
        _ => s.ToString()
    };

    public static string TableColor(TableStatus s) => s switch
    {
        TableStatus.Available => "success",
        TableStatus.Occupied => "primary",
        TableStatus.WaitingPayment => "warning",
        _ => "light"
    };

    public static string Payment(PaymentMethod m) => m == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản";
}
