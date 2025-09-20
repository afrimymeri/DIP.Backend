using DIP.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace DIP.Backend.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) {}

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.Email).IsRequired();
            b.Property(u => u.Name).IsRequired();
            b.Property(u => u.Role).HasMaxLength(32).HasDefaultValue(Roles.User);
        });
        
        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.HasIndex(rt => rt.Token).IsUnique();
            b.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
