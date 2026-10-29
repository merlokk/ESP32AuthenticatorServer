using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.RenameUserDevice;

internal sealed class Endpoint(IdentityDbContext context) : Endpoint<RenameUserDeviceRequest>
{
    public override void Configure()
    {
        Put("/identity/users/{userId}/devices/{id}");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("RenameUserDevice"));
        Summary(summary =>
        {
            summary.Summary = "Name a user's device (admin); an empty name clears it.";
            summary.Description = "device.not-found: not this user's.";
            summary.Responses[204] = "Renamed.";
        });
    }

    public override async Task HandleAsync(RenameUserDeviceRequest request, CancellationToken cancellationToken)
    {
        var name = AccountFields.DeviceNameOf(request.Name);

        if (!name.IsSuccess)
        {
            await IdentityProblem.SendAsync(HttpContext, name.Error, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var userId = Route<Guid>("userId", isRequired: false);
        var id = Route<Guid>("id", isRequired: false);
        var renamed = await context.Devices
            .Where(device => device.Id == id && device.UserId == userId)
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
internal sealed record RenameUserDeviceRequest(string? Name);
