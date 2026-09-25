using Microsoft.EntityFrameworkCore;

namespace Paymob_Integration_Demo.Storage;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentOrder>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity.Property(order => order.Status).HasMaxLength(16).IsRequired();
            entity.Property(order => order.ProductName).IsRequired();
            entity.Property(order => order.CustomerEmail).IsRequired();
            entity.Property(order => order.CustomerPhone).IsRequired();
        });
    }
}