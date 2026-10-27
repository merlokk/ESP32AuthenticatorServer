using AuthServer.Identity.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Infrastructure.Persistence;

/// <summary>
/// The identity schema: users, passkeys, devices, OpenIddict's four tables and the Data
/// Protection key ring. No role tables: a user has one role, a column.
/// </summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid, IdentityUserClaim<Guid>, IdentityUserLogin<Guid>, IdentityUserToken<Guid>, PasskeyCredential>(options),
        IDataProtectionKeyContext
{
    public const string Schema = "identity";

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // The passkey table needs schema version 3, which the base reads from IdentityOptions
        // (IdentityModule sets it). A context built without the host passes the same options.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(user =>
        {
            user.Property(account => account.Role).HasMaxLength(16);

            // The framework's e-mail index is not unique; the address is the sign-in name here.
            user.HasIndex(account => account.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
            user.HasIndex(account => account.Role);
        });

        modelBuilder.Entity<Device>(device =>
        {
            device.HasKey(entity => entity.Id);
            // The column and index keep the first migration's names: renaming them under a running
            // backend is not expand-then-contract.
            device.Property(entity => entity.Serial).HasColumnName("hardware_id").HasMaxLength(64);
            device.HasIndex(entity => entity.Serial).IsUnique().HasDatabaseName("ix_devices_hardware_id");
            device.Property(entity => entity.Mac).HasMaxLength(17);
            device.Property(entity => entity.Firmware).HasMaxLength(FirmwareVersion.MaximumLength);
            device.Property(entity => entity.Status).HasColumnType("jsonb");
            device.HasOne<ApplicationUser>().WithMany().HasForeignKey(entity => entity.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.UseOpenIddict<Guid>();

        modelBuilder.UseSnakeCaseNames();
    }
}
