using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenIddict.Abstractions;

namespace AuthServer.Identity.Infrastructure.Accounts;

/// <summary>
/// Changes that decide who the admins are (the first registration, role, block, delete) run one
/// at a time: a transaction holding one advisory lock. Two admins demoting each other at once
/// leave one standing.
/// </summary>
internal sealed class AccountChanges(
    IdentityDbContext context,
    IOpenIddictTokenManager tokens,
    IOpenIddictAuthorizationManager authorizations)
{
    private const long LockKey = 0x4155_5448_4944; // "AUTHID"

    public async Task<IDbContextTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock({LockKey})", cancellationToken);
        return transaction;
    }

    /// <summary>Inside <see cref="BeginAsync"/>: whether the change leaves no active admin.</summary>
    public async Task<bool> LeavesNoAdminAsync(ApplicationUser target, bool staysActiveAdmin, CancellationToken cancellationToken)
    {
        var activeAdmins = await context.Users.CountAsync(user => user.Role == UserRoles.Admin && user.BlockedAt == null, cancellationToken);

        return LastAdminRule.Refuses(target.IsActiveAdmin, activeAdmins, staysActiveAdmin);
    }

    /// <summary>Ends the user's sessions: refresh tokens and authorizations stop redeeming.</summary>
    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await tokens.RevokeBySubjectAsync(userId.ToString(), cancellationToken);
        await authorizations.RevokeBySubjectAsync(userId.ToString(), cancellationToken);
    }
}
