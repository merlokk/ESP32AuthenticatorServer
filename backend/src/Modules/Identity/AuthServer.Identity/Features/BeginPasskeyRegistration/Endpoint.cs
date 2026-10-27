using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.BeginPasskeyRegistration;

internal sealed class Endpoint(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IdentityDbContext context) : Endpoint<BeginPasskeyRegistrationRequest, BeginPasskeyRegistrationResponse>
{
    public override void Configure()
    {
        Post("/identity/me/passkeys/options");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("BeginPasskeyRegistration"));
        Summary(summary =>
        {
            summary.Summary = "Creation options for a passkey on your account.";
            summary.Description =
                "For PublicKeyCredential.parseCreationOptionsFromJSON() and navigator.credentials.create(); the result "
                + "goes to POST /api/identity/me/passkeys. Refused at ten passkeys, before the ceremony.";
        });
    }

    public override async Task HandleAsync(BeginPasskeyRegistrationRequest request, CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        if (!AccountFields.PasskeyNameOf(request.Name).IsSuccess)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.PasskeyNameInvalid, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        if (await context.UserPasskeys.CountAsync(passkey => passkey.UserId == user.Id, cancellationToken) >= AccountFields.MaximumPasskeys)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.PasskeyLimitReached, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.Email!,
            DisplayName = user.UserName!,
        });

        await Send.OkAsync(new BeginPasskeyRegistrationResponse(options), cancellationToken);
    }
}

internal sealed record BeginPasskeyRegistrationRequest(string? Name);

internal sealed record BeginPasskeyRegistrationResponse(string CreationOptions);
