using AuthServer.Firmware.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Firmware.Infrastructure.Persistence;

/// <summary>The firmware schema: a row per uploaded image, the image in it.</summary>
internal sealed class FirmwareDbContext(DbContextOptions<FirmwareDbContext> options) : DbContext(options)
{
    public const string Schema = "firmware";

    public DbSet<FirmwareRelease> Releases => Set<FirmwareRelease>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<FirmwareRelease>(release =>
        {
            release.ToTable("releases");
            release.HasKey(entity => entity.Id).HasName("pk_releases");
            release.Property(entity => entity.Id).HasColumnName("id");
            release.Property(entity => entity.Board).HasColumnName("board").HasMaxLength(16);
            release.Property(entity => entity.Version).HasColumnName("version").HasMaxLength(32);
            release.Property(entity => entity.Built).HasColumnName("built").HasMaxLength(33);
            release.Property(entity => entity.Idf).HasColumnName("idf").HasMaxLength(32);
            release.Property(entity => entity.Size).HasColumnName("size");
            release.Property(entity => entity.Sha256).HasColumnName("sha256").HasMaxLength(64);
            release.Property(entity => entity.Active).HasColumnName("active");
            release.Property(entity => entity.UploadedAt).HasColumnName("uploaded_at");
            release.Property(entity => entity.UploadedBy).HasColumnName("uploaded_by");
            // Storage EXTERNAL (the migration): uncompressed TOAST, so a Range reads only its slice.
            release.Property(entity => entity.Content).HasColumnName("content");

            release.HasIndex(entity => new { entity.Board, entity.Version })
                .IsUnique()
                .HasDatabaseName("ux_releases_board_version");

            // One active release per board, whatever the race.
            release.HasIndex(entity => entity.Board)
                .IsUnique()
                .HasFilter("active")
                .HasDatabaseName("ux_releases_board_active");
        });
    }
}
