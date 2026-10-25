using MarbookApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Post> Posts { get; set; }
        public DbSet<PostImage> PostImages { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<CommentLike> CommentLikes { get; set; }
        public DbSet<Like> Likes { get; set; }
        public DbSet<Follow> Follows { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<EmailVerification> EmailVerifications { get; set; } = null!;
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<ConversationParticipant> ConversationParticipants { get; set; }
        public DbSet<Message> Messages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasPostgresExtension("citext");
            modelBuilder.HasPostgresExtension("pg_trgm");

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
        
                entity.Property(u => u.Email)
                    .HasColumnType("citext")
                    .IsRequired();

                entity.HasIndex(u => u.Email)
                    .IsUnique();

                entity.Property(u => u.AvatarUrl)
                    .HasMaxLength(500);

                entity.Property(u => u.CoverUrl)
                    .HasMaxLength(500);
                
                entity.Property(u => u.PasswordHash)
                    .HasColumnName("Password")
                    .IsRequired();
                
                entity.Property(u => u.Birthdate)
                    .IsRequired();
                
                entity.Property(u => u.CreatedAt)
                    .HasDefaultValueSql("NOW()");
                entity.Property(u => u.UpdatedAt)
                    .HasDefaultValueSql("NOW()");
            });
            
            modelBuilder.Entity<EmailVerification>()
                .HasOne(ev => ev.User)
                .WithMany()
                .HasForeignKey(ev => ev.UserId)
                .OnDelete(DeleteBehavior.Cascade);
                
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

                entity.Property(p => p.Visibility)
                    .HasDefaultValue(PostVisibility.Public);
                
                entity.HasOne(p => p.User)
                    .WithMany(u => u.Posts)
                    .HasForeignKey(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PostImage>(entity =>
            {
                entity.HasKey(pi => pi.Id);

                entity.Property(pi => pi.Url)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(pi => pi.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.HasOne(pi => pi.Post)
                    .WithMany(p => p.Images)
                    .HasForeignKey(pi => pi.PostId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Comment>(entity =>
            {
                entity.HasKey(c => c.Id);

                entity.Property(c => c.Content)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(c => c.ImageUrl)
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

                entity.HasOne(c => c.ParentComment)
                    .WithMany(c => c.Replies)
                    .HasForeignKey(c => c.ParentCommentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CommentLike>(entity =>
            {
                entity.HasKey(cl => cl.Id);

                entity.HasIndex(cl => new { cl.UserId, cl.CommentId })
                    .IsUnique();

                entity.Property(cl => cl.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.Property(cl => cl.UpdatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.HasOne(cl => cl.User)
                    .WithMany()
                    .HasForeignKey(cl => cl.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(cl => cl.Comment)
                    .WithMany(c => c.Likes)
                    .HasForeignKey(cl => cl.CommentId)
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

            modelBuilder.Entity<Follow>(entity =>
            {
                entity.HasKey(f => new { f.FollowerId, f.FolloweeId });

                entity.Property(f => f.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.HasOne(f => f.Follower)
                    .WithMany(u => u.Following)
                    .HasForeignKey(f => f.FollowerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(f => f.Followee)
                    .WithMany(u => u.Followers)
                    .HasForeignKey(f => f.FolloweeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Notification>(entity =>
            {
                entity.HasKey(n => n.Id);

                entity.Property(n => n.Type)
                    .IsRequired();

                entity.Property(n => n.IsRead)
                    .HasDefaultValue(false);

                entity.Property(n => n.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.HasIndex(n => new { n.RecipientUserId, n.IsRead, n.CreatedAt });

                entity.HasOne(n => n.RecipientUser)
                    .WithMany()
                    .HasForeignKey(n => n.RecipientUserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(n => n.ActorUser)
                    .WithMany()
                    .HasForeignKey(n => n.ActorUserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(n => n.TargetPost)
                    .WithMany()
                    .HasForeignKey(n => n.TargetPostId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(n => n.TargetComment)
                    .WithMany()
                    .HasForeignKey(n => n.TargetCommentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Conversation>(entity =>
            {
                entity.HasKey(c => c.Id);

                entity.Property(c => c.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.Property(c => c.UpdatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.HasIndex(c => c.UpdatedAt);
            });

            modelBuilder.Entity<ConversationParticipant>(entity =>
            {
                entity.HasKey(cp => cp.Id);

                entity.HasIndex(cp => new { cp.ConversationId, cp.UserId })
                    .IsUnique();

                entity.Property(cp => cp.JoinedAt)
                    .HasDefaultValueSql("NOW()");

                entity.HasOne(cp => cp.Conversation)
                    .WithMany(c => c.Participants)
                    .HasForeignKey(cp => cp.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(cp => cp.User)
                    .WithMany(u => u.ConversationParticipants)
                    .HasForeignKey(cp => cp.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Message>(entity =>
            {
                entity.HasKey(m => m.Id);

                entity.Property(m => m.Content)
                    .IsRequired()
                    .HasMaxLength(2000);

                entity.Property(m => m.CreatedAt)
                    .HasDefaultValueSql("NOW()");

                entity.Property(m => m.IsDeleted)
                    .HasDefaultValue(false);

                entity.HasIndex(m => new { m.ConversationId, m.CreatedAt });

                entity.HasOne(m => m.Conversation)
                    .WithMany(c => c.Messages)
                    .HasForeignKey(m => m.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(m => m.SenderUser)
                    .WithMany(u => u.SentMessages)
                    .HasForeignKey(m => m.SenderUserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
