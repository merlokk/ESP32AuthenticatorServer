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
    TimeProvider clock) : EndpointWithoutRequest
{
    // A registration JWS is about 700 characters.
    private const int MaximumBodyLength = 8 * 1024;

    /// <summary>
    /// The firmware's fixed address: <c>POST /register</c>, body a bare JWS (<c>application/jwt</c>).
    /// The ingress sends it to the backend directly. The device reads only the status code: 2xx
    /// registered, anything else refused. The body holds the TOTP secret: never logged.
    /// </summary>
    public override void Configure()
    {
        Post("/register");
        RoutePrefixOverride(string.Empty);
        AllowAnonymous();
        Description(builder => builder.ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var verified = DeviceRegistration.Verify(await ReadTokenAsync(cancellationToken), clock.GetUtcNow());

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

        var device = await context.Devices.SingleOrDefaultAsync(entity => entity.Serial == registration.Serial, cancellationToken);

        if (device is not null && device.UserId != user.Id)
        {
            await RefuseAsync(IdentityErrors.DeviceTaken, cancellationToken);
            return;
        }

        if (device is null)
        {
            device = new Device { Id = Guid.CreateVersion7(), UserId = user.Id, Serial = registration.Serial };
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

    // The body as text, or null past the limit.
    private async Task<string?> ReadTokenAsync(CancellationToken cancellationToken)
    {
        var buffer = new char[MaximumBodyLength + 1];
        using var reader = new StreamReader(HttpContext.Request.Body, System.Text.Encoding.ASCII);
        var length = await reader.ReadBlockAsync(buffer, cancellationToken);

        return length > MaximumBodyLength ? null : new string(buffer, 0, length);
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
            error == IdentityErrors.RegistrationInvalid || error == IdentityErrors.RegistrationStale ? StatusCodes.Status400BadRequest
            : error == IdentityErrors.DeviceTaken ? StatusCodes.Status409Conflict
            : StatusCodes.Status403Forbidden,
            cancellationToken);
}

internal sealed record RegisterDeviceResponse(Guid DeviceId, string ClientId);
