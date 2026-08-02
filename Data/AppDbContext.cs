using MarbookApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasPostgresExtension("citext");

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);

                entity.Property(u => u.Name)
                    .IsRequired()
                    .HasMaxLength(50);
                
                entity.Property(u => u.Username)
                    .HasColumnType("citext");

                entity.HasIndex(u => u.Username)
                    .IsUnique();
        
                entity.Property(u => u.Email).
                    HasColumnType("citext")
                    .IsRequired();

                entity.HasIndex(u => u.Email)
                    .IsUnique();
                
                entity.Property(u => u.Password)
                    .IsRequired();
                
                entity.Property(u => u.Birthdate)
                    .IsRequired();
                
                entity.Property(u => u.CreatedAt)
                    .HasDefaultValueSql("NOW()");
                entity.Property(u => u.UpdatedAt)
                    .HasDefaultValueSql("NOW()");
            });
        }
    }
}