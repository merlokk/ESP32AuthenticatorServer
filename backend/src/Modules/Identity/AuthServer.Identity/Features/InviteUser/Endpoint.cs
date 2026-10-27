using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AuthServer.Identity.Features.InviteUser;

internal sealed class Endpoint(
    UserManager<ApplicationUser> users,
    IdentitySettings settings,
    TimeProvider clock) : Endpoint<InviteUserRequest, InviteUserResponse>
{
    public override void Configure()
    {
        Post("/identity/users");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("InviteUser"));
        Summary(summary =>
        {
            summary.Summary = "Invite a user (admin): an account with no device and a registration window.";
            summary.Description = "The window is InviteWindow (24 h); the user registers a device with this e-mail before it closes.";
            summary.Responses[201] = "Invited.";
        });
    }

    public override async Task HandleAsync(InviteUserRequest request, CancellationToken cancellationToken)
    {
        var email = AccountFields.EmailOf(request.Email);
        var userName = AccountFields.UserNameOf(request.UserName);
        var role = UserRoles.Of(request.Role ?? UserRoles.User);

        if (!email.IsSuccess || !userName.IsSuccess || role is null)
        {
            await RefuseAsync(
                !email.IsSuccess ? email.Error : !userName.IsSuccess ? userName.Error : IdentityErrors.RoleInvalid,
                StatusCodes.Status400BadRequest,
                cancellationToken);
            return;
        }

        if (await users.FindByEmailAsync(email.Value) is not null)
        {
            await RefuseAsync(IdentityErrors.EmailTaken, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        if (await users.FindByNameAsync(userName.Value) is not null)
        {
            await RefuseAsync(IdentityErrors.UserNameTaken, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        var now = clock.GetUtcNow();

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            Email = email.Value,
            UserName = userName.Value,
            Role = role,
            CreatedAt = now,
            RegistrationOpenUntil = RegistrationWindow.Until(now, hasDevices: false, settings.InviteWindow, settings.DeviceWindow),
        };

        // The pre-checks answer the common case; the unique indexes answer a race.
        if (!(await users.CreateAsync(user)).Succeeded)
        {
            await RefuseAsync(IdentityErrors.EmailTaken, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        await Send.ResponseAsync(
            new InviteUserResponse(user.Id, user.RegistrationOpenUntil.Value), StatusCodes.Status201Created, cancellationToken);
    }

    private Task RefuseAsync(Error error, int status, CancellationToken cancellationToken) =>
        IdentityProblem.SendAsync(HttpContext, error, status, cancellationToken);
}

internal sealed record InviteUserRequest(string? Email, string? UserName, string? Role);

internal sealed record InviteUserResponse(Guid Id, DateTimeOffset RegistrationOpenUntil);
