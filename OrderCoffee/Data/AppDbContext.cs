using Microsoft.EntityFrameworkCore;
using OrderCoffee.Models;
using OrderCoffee.Services;

namespace OrderCoffee.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, EventHub hub) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CafeTable> Tables => Set<CafeTable>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<UserVoucher> UserVouchers => Set<UserVoucher>();
    public DbSet<PointTransaction> PointTransactions => Set<PointTransaction>();
    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Phone).IsUnique();
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<CafeTable>().HasIndex(t => t.Code).IsUnique();
        b.Entity<Voucher>().HasIndex(v => v.Code).IsUnique();
        b.Entity<Order>().HasIndex(o => o.Code).IsUnique();
        b.Entity<UserAddress>().HasIndex(a => new { a.UserId, a.Phone, a.Address }).IsUnique();
        b.Entity<UserAddress>().HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Product>().HasOne(p => p.Category).WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>().HasOne(o => o.User).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.Entity<Order>().HasOne(o => o.Table).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.Entity<Order>().HasOne(o => o.Voucher).WithMany().OnDelete(DeleteBehavior.SetNull);
    }

    // Mọi thay đổi đơn / món / bàn đều tự phát sự kiện realtime (SSE), không cần nhớ gọi ở từng controller
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var touched = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                        && e.Entity is Order or OrderItem or CafeTable)
            .Select(e => e.Entity).ToList();

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (touched.Count > 0) await PublishAsync(touched);
        return result;
    }

    private async Task PublishAsync(List<object> touched)
    {
        try
        {
            var orderCodes = new HashSet<string>();
            var tableIds = new HashSet<int>();
            void Track(Order o)
            {
                orderCodes.Add(o.Code);
                if (o.TableId is int t) tableIds.Add(t);
            }

            foreach (var e in touched)
            {
                switch (e)
                {
                    case Order o: Track(o); break;
                    case CafeTable t: tableIds.Add(t.Id); break;
                    case OrderItem i:
                        var owner = i.Order ?? await Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == i.OrderId);
                        if (owner != null) Track(owner);
                        break;
                }
            }

            hub.Publish("staff");
            foreach (var c in orderCodes) hub.Publish("order:" + c);
            foreach (var t in tableIds) hub.Publish("table:" + t);
        }
        catch { /* thông báo realtime lỗi không được làm hỏng việc lưu dữ liệu */ }
    }
}
