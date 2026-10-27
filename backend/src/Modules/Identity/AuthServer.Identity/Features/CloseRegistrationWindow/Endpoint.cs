using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.CloseRegistrationWindow;

internal sealed class Endpoint(IdentityDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/identity/users/{id}/registration-window");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("CloseRegistrationWindow"));
        Summary(summary => summary.Summary = "Withdraw an invitation or a device window (admin).");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<Guid>("id");

        var closed = await context.Users
            .Where(user => user.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.RegistrationOpenUntil, (DateTimeOffset?)null), cancellationToken);

        if (closed == 0)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.UserNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
