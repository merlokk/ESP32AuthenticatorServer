using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.OpenRegistrationWindow;

internal sealed class Endpoint(
    IdentityDbContext context,
    IdentitySettings settings,
    TimeProvider clock) : EndpointWithoutRequest<RegistrationWindowResponse>
{
    public override void Configure()
    {
        Put("/identity/users/{id}/registration-window");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("OpenRegistrationWindow"));
        Summary(summary =>
        {
            summary.Summary = "Let a device register to this user for a while (admin).";
            summary.Description = "Extends an invitation by InviteWindow while the user has no device, else opens DeviceWindow.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<Guid>("id");

        if (await context.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken) is not { } user)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.UserNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var hasDevices = await context.Devices.AnyAsync(device => device.UserId == id, cancellationToken);
        user.RegistrationOpenUntil = RegistrationWindow.Until(clock.GetUtcNow(), hasDevices, settings.InviteWindow, settings.DeviceWindow);
        await context.SaveChangesAsync(cancellationToken);

        await Send.OkAsync(new RegistrationWindowResponse(user.RegistrationOpenUntil.Value), cancellationToken);
    }
}

internal sealed record RegistrationWindowResponse(DateTimeOffset RegistrationOpenUntil);
