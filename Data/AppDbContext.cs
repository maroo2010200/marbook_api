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
        public DbSet<Post> Posts { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Like> Likes { get; set; }

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
            
            modelBuilder.Entity<Post>(entity =>
            {
                entity.HasKey(p => p.Id);

                entity.Property(p => p.Content)
                    .IsRequired()
                    .HasMaxLength(500);
                
                entity.Property(p => p.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.Property(p => p.UpdatedAt)
                    .HasDefaultValueSql("NOW()");
                
                entity.HasOne(p => p.User)
                    .WithMany(u => u.Posts)
                    .HasForeignKey(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Comment>(entity =>
            {
                entity.HasKey(c => c.Id);

                entity.Property(c => c.Content)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(c => c.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.Property(c => c.UpdatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.HasOne(c => c.User)
                    .WithMany(u => u.Comments)
                    .HasForeignKey(c => c.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(c => c.Post)
                    .WithMany(p => p.Comments)
                    .HasForeignKey(c => c.PostId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Like>(entity =>
            {
                entity.HasKey(l => l.Id);

                entity.HasIndex(l => new { l.UserId, l.PostId })
                    .IsUnique();

                entity.Property(l => l.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.Property(l => l.UpdatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.HasOne(l => l.User)
                    .WithMany(u => u.Likes)
                    .HasForeignKey(l => l.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(l => l.Post)
                    .WithMany(p => p.Likes)
                    .HasForeignKey(l => l.PostId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}