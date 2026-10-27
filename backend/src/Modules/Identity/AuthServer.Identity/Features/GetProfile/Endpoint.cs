using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AuthServer.Identity.Features.GetProfile;

internal sealed class Endpoint(UserManager<ApplicationUser> users) : EndpointWithoutRequest<ProfileResponse>
{
    public override void Configure()
    {
        Get("/identity/me");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("GetProfile"));
        Summary(summary => summary.Summary = "The signed-in user.");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(
            new ProfileResponse(user.Id, user.UserName!, user.Email!, user.Role, user.RegistrationOpenUntil, user.Role == UserRoles.Admin),
            cancellationToken);
    }
}

/// <param name="CanAdminister">What the caller may do: the UI never infers it from the role name.</param>
internal sealed record ProfileResponse(
    Guid Id, string UserName, string Email, string Role, DateTimeOffset? RegistrationOpenUntil, bool CanAdminister);
