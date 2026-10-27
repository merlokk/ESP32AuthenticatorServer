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
    TimeProvider clock) : Endpoint<OpenRegistrationWindowRequest, RegistrationWindowResponse>
{
    public override void Configure()
    {
        Put("/identity/users/{id}/registration-window");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("OpenRegistrationWindow"));
        Summary(summary =>
        {
            summary.Summary = "Let a device register to this user for a while (admin).";
            summary.Description =
                "Opens or extends the window from now for ?hours= (1 to 24). Without it: InviteWindow while the user "
                + "has no device, else DeviceWindow.";
        });
    }

    public override async Task HandleAsync(OpenRegistrationWindowRequest request, CancellationToken cancellationToken)
    {
        TimeSpan? chosen = null;

        if (request.Hours is { } hours)
        {
            var length = RegistrationWindow.HoursOf(hours);

            if (!length.IsSuccess)
            {
                await IdentityProblem.SendAsync(HttpContext, length.Error, StatusCodes.Status400BadRequest, cancellationToken);
                return;
            }

            chosen = length.Value;
        }

        var id = Route<Guid>("id");

        if (await context.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken) is not { } user)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.UserNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var now = clock.GetUtcNow();

        if (chosen is { } window)
        {
            user.RegistrationOpenUntil = now + window;
        }
        else
        {
            var hasDevices = await context.Devices.AnyAsync(device => device.UserId == id, cancellationToken);
            user.RegistrationOpenUntil = RegistrationWindow.Until(now, hasDevices, settings.InviteWindow, settings.DeviceWindow);
        }

        await context.SaveChangesAsync(cancellationToken);

        await Send.OkAsync(new RegistrationWindowResponse(user.RegistrationOpenUntil.Value), cancellationToken);
    }
}

internal sealed class OpenRegistrationWindowRequest
{
    /// <summary>1 to 24; without it the server's default for this user. A query parameter: the PUT has no body.</summary>
    [QueryParam]
    public int? Hours { get; init; }
}

internal sealed record RegistrationWindowResponse(DateTimeOffset RegistrationOpenUntil);
