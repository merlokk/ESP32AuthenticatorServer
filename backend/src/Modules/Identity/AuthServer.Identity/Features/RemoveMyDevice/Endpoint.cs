using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Devices;
using AuthServer.Identity.Infrastructure.Http;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AuthServer.Identity.Features.RemoveMyDevice;

internal sealed class Endpoint(UserManager<ApplicationUser> users, DeviceRemoval removal) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/identity/me/devices/{id}");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("RemoveMyDevice"));
        Summary(summary =>
        {
            summary.Summary = "Remove one of your devices; it signs in no more.";
            summary.Description =
                "Its token lapses within minutes; its stores stay. It may register again through a window. "
                + "The last one may go too: a passkey still signs in.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        if (!await removal.RemoveAsync(user.Id, Route<Guid>("id", isRequired: false), cancellationToken))
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.DeviceNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
