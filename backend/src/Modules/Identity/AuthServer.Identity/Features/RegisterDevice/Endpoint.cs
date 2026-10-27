using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Accounts;
using AuthServer.Identity.Infrastructure.Devices;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using AuthServer.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.RegisterDevice;

internal sealed class Endpoint(
    IdentityDbContext context,
    UserManager<ApplicationUser> users,
    AccountChanges changes,
    DeviceClients clients,
    TotpSecretProtector protector,
    TimeProvider clock) : Endpoint<RegisterDeviceRequest, RegisterDeviceResponse>
{
    public override void Configure()
    {
        Post("/identity/devices/register");
        AllowAnonymous();
        Options(route => route.WithTags("Identity").WithName("RegisterDevice"));
        Summary(summary =>
        {
            summary.Summary = "Register a device to the account with this e-mail (called by the device).";
            summary.Description =
                "The fields are signed with the device's P-256 key (architecture.md §4.6). Needs an open registration "
                + "window on the account, except for the very first account, which becomes an admin. A device with "
                + "the same hardware id on the same account is replaced. Every refusal looks the same.";
        });
    }

    public override async Task HandleAsync(RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        var verified = DeviceRegistration.Verify(
            request.Email, request.HardwareId, request.Mac, request.TotpSecret, request.PublicKey, request.Signature);

        if (!verified.IsSuccess)
        {
            await RefuseAsync(verified.Error, cancellationToken);
            return;
        }

        var registration = verified.Value;
        var now = clock.GetUtcNow();

        await using var transaction = await changes.BeginAsync(cancellationToken);

        var bootstrap = !await context.Users.AnyAsync(cancellationToken);

        var user = bootstrap
            ? await CreateFirstAdminAsync(registration.Email, now)
            : await users.FindByEmailAsync(registration.Email);

        if (user is null || user.BlockedAt is not null || (!bootstrap && !RegistrationWindow.IsOpen(user.RegistrationOpenUntil, now)))
        {
            await RefuseAsync(IdentityErrors.RegistrationRefused, cancellationToken);
            return;
        }

        var device = await context.Devices.SingleOrDefaultAsync(entity => entity.HardwareId == registration.HardwareId, cancellationToken);

        if (device is not null && device.UserId != user.Id)
        {
            await RefuseAsync(IdentityErrors.DeviceTaken, cancellationToken);
            return;
        }

        if (device is null)
        {
            device = new Device { Id = Guid.CreateVersion7(), UserId = user.Id, HardwareId = registration.HardwareId };
            context.Devices.Add(device);
        }

        device.Mac = registration.Mac;
        device.PublicKey = registration.PublicKey;
        device.TotpSecret = protector.Protect(registration.TotpSecret);
        device.LastTotpStep = 0;
        device.RegisteredAt = now;

        user.RegistrationOpenUntil = null;

        await context.SaveChangesAsync(cancellationToken);
        await clients.UpsertAsync(device, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await Send.OkAsync(new RegisterDeviceResponse(device.Id, device.Id.ToString()), cancellationToken);
    }

    // No users yet: the first device makes the first admin, no window needed.
    private async Task<ApplicationUser?> CreateFirstAdminAsync(string email, DateTimeOffset now)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            UserName = AccountFields.DefaultUserNameFor(email),
            Role = UserRoles.Admin,
            CreatedAt = now,
        };

        return (await users.CreateAsync(user)).Succeeded ? user : null;
    }

    private Task RefuseAsync(Error error, CancellationToken cancellationToken) =>
        IdentityProblem.SendAsync(
            HttpContext,
            error,
            error == IdentityErrors.RegistrationInvalid ? StatusCodes.Status400BadRequest
            : error == IdentityErrors.DeviceTaken ? StatusCodes.Status409Conflict
            : StatusCodes.Status403Forbidden,
            cancellationToken);
}

internal sealed record RegisterDeviceRequest(
    string? Email, string? HardwareId, string? Mac, string? TotpSecret, string? PublicKey, string? Signature);

internal sealed record RegisterDeviceResponse(Guid DeviceId, string ClientId);
