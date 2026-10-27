using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Persistence;
using IdentityDbContext = AuthServer.Identity.Infrastructure.Persistence.IdentityDbContext;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace AuthServer.Identity.Infrastructure.Accounts;

/// <summary>The framework's user-only store over this module's context and passkey type.</summary>
internal sealed class AccountStore(IdentityDbContext context, IdentityErrorDescriber? describer = null)
    : UserOnlyStore<ApplicationUser, IdentityDbContext, Guid, IdentityUserClaim<Guid>, IdentityUserLogin<Guid>, IdentityUserToken<Guid>, PasskeyCredential>(
        context, describer);
