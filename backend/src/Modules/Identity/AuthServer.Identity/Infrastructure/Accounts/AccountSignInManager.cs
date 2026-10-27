using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthServer.Identity.Infrastructure.Accounts;

/// <summary>
/// The framework's sign-in manager, where a blocked account cannot sign in (every path: TOTP,
/// passkey, the identity cookie at authorize) and a successful sign-in is stamped.
/// </summary>
internal sealed class AccountSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    IdentityDbContext context,
    TimeProvider clock)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user) =>
        user.BlockedAt is null && await base.CanSignInAsync(user);

    public override async Task SignInWithClaimsAsync(
        ApplicationUser user, AuthenticationProperties? authenticationProperties, IEnumerable<System.Security.Claims.Claim> additionalClaims)
    {
        await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);

        await context.Users
            .Where(account => account.Id == user.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(account => account.LastSignInAt, clock.GetUtcNow()));
    }
}
