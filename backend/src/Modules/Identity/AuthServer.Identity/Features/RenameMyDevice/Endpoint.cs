using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.RenameMyDevice;

internal sealed class Endpoint(UserManager<ApplicationUser> users, IdentityDbContext context) : Endpoint<RenameMyDeviceRequest>
{
    public override void Configure()
    {
        Put("/identity/me/devices/{id}");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("RenameMyDevice"));
        Summary(summary =>
        {
            summary.Summary = "Name one of your devices; an empty name clears it.";
            summary.Responses[204] = "Renamed.";
        });
    }

    public override async Task HandleAsync(RenameMyDeviceRequest request, CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var name = AccountFields.DeviceNameOf(request.Name);

        if (!name.IsSuccess)
        {
            await IdentityProblem.SendAsync(HttpContext, name.Error, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var id = Route<Guid>("id", isRequired: false);
        var renamed = await context.Devices
            .Where(device => device.Id == id && device.UserId == user.Id)
            .ExecuteUpdateAsync(device => device.SetProperty(entity => entity.Name, name.Value), cancellationToken);

        if (renamed == 0)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.DeviceNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}

/// <param name="Name">At most 64 characters; empty or null clears it.</param>
internal sealed record RenameMyDeviceRequest(string? Name);
