using Microsoft.EntityFrameworkCore;

namespace KufarBot.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<UserSearchEntity> UserSearches => Set<UserSearchEntity>();
    public DbSet<PollingFeedEntity> PollingFeeds => Set<PollingFeedEntity>();
    public DbSet<DeliveredAdEntity> DeliveredAds => Set<DeliveredAdEntity>();
    public DbSet<NotificationOutboxEntity> NotificationOutbox => Set<NotificationOutboxEntity>();
    public DbSet<WatchedAdEntity> WatchedAds => Set<WatchedAdEntity>();
    public DbSet<UsageEventEntity> UsageEvents => Set<UsageEventEntity>();
    public DbSet<UserTemplateEntity> UserTemplates => Set<UserTemplateEntity>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // Telegram ChatId
        });

        modelBuilder.Entity<UserSearchEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User)
                  .WithMany(u => u.Searches)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PollingFeedEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SubcategoryId, e.CityId }).IsUnique();
        });

        modelBuilder.Entity<DeliveredAdEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.AdId }).IsUnique();
            entity.HasOne(e => e.User)
                  .WithMany(u => u.DeliveredAds)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationOutboxEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Status, e.NextAttemptAtUtc });
            entity.HasOne(e => e.User)
                  .WithMany(u => u.OutboxItems)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WatchedAdEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.AdId }).IsUnique();
            entity.HasIndex(e => e.LastCheckedUtc);
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UsageEventEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.EventType, e.OccurredAtUtc });
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<UserTemplateEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(64);
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
