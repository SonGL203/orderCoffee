using Microsoft.AspNetCore.Identity;
using OrderCoffee.Models;

namespace OrderCoffee.Data;

// Tài khoản test (chỉ dùng khi phát triển) – xem README.md
public static class SeedData
{
    public static void Initialize(AppDbContext db)
    {
                if (db.Users.Any()) return;

        var hasher = new PasswordHasher<User>();
        User NewUser(string name, string phone, string email, string role, string password, int points = 0)
        {
            var u = new User { FullName = name, Phone = phone, Email = email, Role = role, Points = points };
            u.PasswordHash = hasher.HashPassword(u, password);
            return u;
        }

        var customer = NewUser("Khách Test", "0900000003", "khach@coffee.vn", Roles.Customer, "Khach@123", 150);
        db.Users.AddRange(
            NewUser("Quản trị viên", "0900000001", "admin@coffee.vn", Roles.Admin, "Admin@123"),
            NewUser("Nhân viên A", "0900000002", "staff@coffee.vn", Roles.Staff, "Staff@123"),
            customer);
        db.SaveChanges();
        db.PointTransactions.Add(new PointTransaction { UserId = customer.Id, Amount = 150, Reason = "Điểm tặng tài khoản test" });

        var coffee = new Category { Name = "Cà phê", Icon = "☕", SortOrder = 1 };
        var tea = new Category { Name = "Trà", Icon = "🍵", SortOrder = 2 };
        var ice = new Category { Name = "Đá xay", Icon = "🥤", SortOrder = 3 };
        var cake = new Category { Name = "Bánh", Icon = "🍰", SortOrder = 4 };
        db.Categories.AddRange(coffee, tea, ice, cake);

        db.Products.AddRange(
            new Product { Category = coffee, Name = "Cà phê đen", Price = 25000, Description = "Robusta rang mộc, đậm vị" },
            new Product { Category = coffee, Name = "Cà phê sữa", Price = 29000, Description = "Phin truyền thống với sữa đặc" },
            new Product { Category = coffee, Name = "Bạc xỉu", Price = 32000, Description = "Nhiều sữa, ít cà phê" },
            new Product { Category = coffee, Name = "Latte", Price = 45000, Description = "Espresso và sữa tươi đánh mịn" },
            new Product { Category = coffee, Name = "Cappuccino", Price = 45000 },
            new Product { Category = tea, Name = "Trà đào cam sả", Price = 39000, Description = "Best seller" },
            new Product { Category = tea, Name = "Trà vải", Price = 39000 },
            new Product { Category = tea, Name = "Trà sữa trân châu", Price = 35000 },
            new Product { Category = ice, Name = "Cookie đá xay", Price = 49000 },
            new Product { Category = ice, Name = "Matcha đá xay", Price = 52000 },
            new Product { Category = cake, Name = "Bánh tiramisu", Price = 35000 },
            new Product { Category = cake, Name = "Croissant bơ", Price = 25000 },
            new Product { Category = cake, Name = "Bánh mì que", Price = 15000, IsAvailable = false });

        for (var i = 1; i <= 8; i++)
            db.Tables.Add(new CafeTable { Name = $"Bàn {i}", Code = NewTableCode() });

        db.Vouchers.AddRange(
            new Voucher { Code = "WELCOME10", Name = "Chào bạn mới", Type = VoucherType.Percent, Value = 10, MaxDiscount = 30000, MinOrder = 50000, PerUserLimit = 1, StartAt = DateTime.Today.AddDays(-1), EndAt = DateTime.Today.AddYears(1) },
            new Voucher { Code = "GIAM20K", Name = "Giảm 20K đơn từ 100K", Type = VoucherType.Fixed, Value = 20000, MinOrder = 100000, UsageLimit = 100, StartAt = DateTime.Today.AddDays(-1), EndAt = DateTime.Today.AddMonths(3) },
            new Voucher { Code = "DOI50", Name = "Đổi 50 điểm – giảm 10K", Type = VoucherType.Fixed, Value = 10000, PointCost = 50, StartAt = DateTime.Today.AddDays(-1), EndAt = DateTime.Today.AddYears(1) },
            new Voucher { Code = "DOI100", Name = "Đổi 100 điểm – giảm 25K", Type = VoucherType.Fixed, Value = 25000, MinOrder = 50000, PointCost = 100, StartAt = DateTime.Today.AddDays(-1), EndAt = DateTime.Today.AddYears(1) },
            new Voucher { Code = "DOI200", Name = "Đổi 200 điểm – giảm 20%", Type = VoucherType.Percent, Value = 20, MaxDiscount = 50000, PointCost = 200, StartAt = DateTime.Today.AddDays(-1), EndAt = DateTime.Today.AddYears(1) });

        db.SaveChanges();
    }

    public static string NewTableCode()
    {
        const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }
}
