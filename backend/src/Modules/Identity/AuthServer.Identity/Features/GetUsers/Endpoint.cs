using AuthServer.Identity.Contracts;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.GetUsers;

internal sealed class Endpoint(IdentityDbContext context) : EndpointWithoutRequest<UsersResponse>
{
    public override void Configure()
    {
        Get("/identity/users");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("GetUsers"));
        Summary(summary => summary.Summary = "Every account, by user name (admin).");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var users = await context.Users
            .OrderBy(user => user.NormalizedUserName)
            .Select(user => new UserRow(
                user.Id,
                user.UserName!,
                user.Email!,
                user.Role,
                user.BlockedAt != null,
                user.RegistrationOpenUntil,
                context.Devices.Count(device => device.UserId == user.Id),
                context.UserPasskeys.Count(passkey => passkey.UserId == user.Id),
                user.CreatedAt,
                user.LastSignInAt))
            .ToListAsync(cancellationToken);

        await Send.OkAsync(new UsersResponse(users), cancellationToken);
    }
}

internal sealed record UsersResponse(IReadOnlyList<UserRow> Users);

internal sealed record UserRow(
    Guid Id,
    string UserName,
    string Email,
    string Role,
    bool Blocked,
    DateTimeOffset? RegistrationOpenUntil,
    int Devices,
    int Passkeys,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt);
