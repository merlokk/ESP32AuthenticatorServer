using System.Buffers.Text;
using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.GetPasskeys;

internal sealed class Endpoint(UserManager<ApplicationUser> users, IdentityDbContext context) : EndpointWithoutRequest<PasskeysResponse>
{
    public override void Configure()
    {
        Get("/identity/me/passkeys");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("GetPasskeys"));
        Summary(summary => summary.Summary = "Your passkeys. The id is the credential id, base64url.");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var passkeys = await context.UserPasskeys
            .AsNoTracking()
            .Where(passkey => passkey.UserId == user.Id)
            .ToListAsync(cancellationToken);

        await Send.OkAsync(
            new PasskeysResponse([.. passkeys
                .OrderBy(passkey => passkey.Data.CreatedAt)
                .Select(passkey => new Passkey(
                    Base64Url.EncodeToString(passkey.CredentialId),
                    passkey.Data.Name ?? string.Empty,
                    passkey.Data.CreatedAt,
                    passkey.LastUsedAt))]),
            cancellationToken);
    }
}

internal sealed record PasskeysResponse(IReadOnlyList<Passkey> Passkeys);

internal sealed record Passkey(string Id, string Name, DateTimeOffset AddedAt, DateTimeOffset? LastUsedAt);
