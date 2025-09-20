using DIP.Backend.Data;
using DIP.Backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace DIP.Backend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
public class ApplicationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.8");

        modelBuilder.Entity(typeof(User), b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("Name").IsRequired().HasColumnType("TEXT");
            b.Property<string>("Email").IsRequired().HasColumnType("TEXT");
            b.HasKey("Id");
            b.ToTable("Users");
        });
    }
}