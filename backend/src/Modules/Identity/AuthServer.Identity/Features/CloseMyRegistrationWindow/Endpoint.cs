using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AuthServer.Identity.Features.CloseMyRegistrationWindow;

internal sealed class Endpoint(UserManager<ApplicationUser> users, IdentityDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/identity/me/registration-window");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("CloseMyRegistrationWindow"));
        Summary(summary => summary.Summary = "No device may register to your account until a window opens again.");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        user.RegistrationOpenUntil = null;
        await context.SaveChangesAsync(cancellationToken);

        await Send.NoContentAsync(cancellationToken);
    }
}
