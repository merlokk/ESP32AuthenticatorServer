using AuthServer.Sync.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Sync.Infrastructure.Persistence;

/// <summary>The sync schema: a row per store (its head) and its last versions with the files.</summary>
internal sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public const string Schema = "sync";

    public DbSet<SyncedStore> Stores => Set<SyncedStore>();

    public DbSet<StoreVersion> Versions => Set<StoreVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SyncedStore>(store =>
        {
            store.ToTable("stores");
            store.HasKey(entity => entity.Id).HasName("pk_stores");
            store.Property(entity => entity.Id).HasColumnName("id");
            store.Property(entity => entity.UserId).HasColumnName("user_id");
            store.Property(entity => entity.StoreId).HasColumnName("store_id").HasMaxLength(16);
            store.Property(entity => entity.SeedId).HasColumnName("seed_id").HasMaxLength(8);
            store.Property(entity => entity.Kind).HasColumnName("kind").HasConversion<short>();
            store.Property(entity => entity.HeadVersion).HasColumnName("head_version");
            store.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");

            // The store's identity: one row whatever the race (the upload upserts on it).
            store.HasIndex(entity => new { entity.UserId, entity.StoreId, entity.SeedId, entity.Kind })
                .IsUnique()
                .HasDatabaseName("ux_stores_user_store_seed_kind");
        });

        modelBuilder.Entity<StoreVersion>(version =>
        {
            version.ToTable("store_versions");
            version.HasKey(entity => entity.Id).HasName("pk_store_versions");
            version.Property(entity => entity.Id).HasColumnName("id");
            version.Property(entity => entity.StoreRef).HasColumnName("store_ref");
            version.Property(entity => entity.Version).HasColumnName("version");
            version.Property(entity => entity.Modified).HasColumnName("modified");
            version.Property(entity => entity.Cipher).HasColumnName("cipher").HasConversion<short>();
            version.Property(entity => entity.Length).HasColumnName("length");
            version.Property(entity => entity.KdfIterations).HasColumnName("kdf_iterations");
            version.Property(entity => entity.KeyId).HasColumnName("key_id").HasMaxLength(16);
            version.Property(entity => entity.UploadedByDevice).HasColumnName("uploaded_by_device");
            version.Property(entity => entity.UploadedAt).HasColumnName("uploaded_at");
            version.Property(entity => entity.Content).HasColumnName("content");

            // A version is in a store once: a second writer of the same counter fails here too.
            version.HasIndex(entity => new { entity.StoreRef, entity.Version })
                .IsUnique()
                .HasDatabaseName("ux_store_versions_store_version");
            version.HasOne<SyncedStore>().WithMany().HasForeignKey(entity => entity.StoreRef)
                .HasConstraintName("fk_store_versions_stores").OnDelete(DeleteBehavior.Cascade);
        });
    }
}
