using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.OpenMyRegistrationWindow;

internal sealed class Endpoint(
    UserManager<ApplicationUser> users,
    IdentityDbContext context,
    IdentitySettings settings,
    TimeProvider clock) : EndpointWithoutRequest<RegistrationWindowResponse>
{
    public override void Configure()
    {
        Put("/identity/me/registration-window");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("OpenMyRegistrationWindow"));
        Summary(summary =>
        {
            summary.Summary = "Let a device register to your account for a while.";
            summary.Description = "Opens or extends the window from now: DeviceWindow (1 h) once you have a device, InviteWindow before.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var hasDevices = await context.Devices.AnyAsync(device => device.UserId == user.Id, cancellationToken);
        user.RegistrationOpenUntil = RegistrationWindow.Until(clock.GetUtcNow(), hasDevices, settings.InviteWindow, settings.DeviceWindow);
        await context.SaveChangesAsync(cancellationToken);

        await Send.OkAsync(new RegistrationWindowResponse(user.RegistrationOpenUntil.Value), cancellationToken);
    }
}

internal sealed record RegistrationWindowResponse(DateTimeOffset RegistrationOpenUntil);
