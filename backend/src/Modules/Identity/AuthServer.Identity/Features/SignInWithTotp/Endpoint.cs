using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Devices;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.SignInWithTotp;

internal sealed class Endpoint(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IdentityDbContext context,
    TotpSecretProtector protector,
    TimeProvider clock) : Endpoint<SignInWithTotpRequest, SignInWithTotpResponse>
{
    public override void Configure()
    {
        Post("/identity/sign-in/totp");
        AllowAnonymous();
        Options(route => route.WithTags("Identity").WithName("SignInWithTotp"));
        Summary(summary =>
        {
            summary.Summary = "Sign in with an e-mail and the code one of the account's devices shows.";
            summary.Description =
                "Sets the authorization server's cookie, which /connect/authorize then turns into a code. "
                + "A code is accepted once per device; wrong codes count towards a lockout. Every refusal is the "
                + "same 401: callers must not branch on the cause.";
            summary.Responses[200] = "Signed in. The credential is the Set-Cookie header.";
        });
    }

    public override async Task HandleAsync(SignInWithTotpRequest request, CancellationToken cancellationToken)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var user = email.Length == 0 ? null : await users.FindByEmailAsync(email);

        if (user is null || await users.IsLockedOutAsync(user) || !await signIn.CanSignInAsync(user))
        {
            await RefuseAsync(cancellationToken);
            return;
        }

        var now = clock.GetUtcNow();
        var devices = await context.Devices.AsNoTracking().Where(device => device.UserId == user.Id).ToListAsync(cancellationToken);

        var match = devices
            .Select(device => (device.Id, Step: Totp.Match(protector.Unprotect(device.TotpSecret), request.Code, now, device.LastTotpStep)))
            .FirstOrDefault(candidate => candidate.Step is not null);

        // The step is claimed atomically: of two requests with one code, one wins.
        var claimed = match.Step is { } step && await context.Devices
            .Where(device => device.Id == match.Id && device.LastTotpStep < step)
            .ExecuteUpdateAsync(update => update.SetProperty(device => device.LastTotpStep, step), cancellationToken) == 1;

        if (!claimed)
        {
            await users.AccessFailedAsync(user);
            await RefuseAsync(cancellationToken);
            return;
        }

        await users.ResetAccessFailedCountAsync(user);
        await signIn.SignInAsync(user, isPersistent: true);

        await Send.OkAsync(new SignInWithTotpResponse(SignedIn: true), cancellationToken);
    }

    private Task RefuseAsync(CancellationToken cancellationToken) =>
        IdentityProblem.SendAsync(HttpContext, IdentityErrors.SignInRefused, StatusCodes.Status401Unauthorized, cancellationToken);
}

internal sealed record SignInWithTotpRequest(string? Email, string? Code);

internal sealed record SignInWithTotpResponse(bool SignedIn);
