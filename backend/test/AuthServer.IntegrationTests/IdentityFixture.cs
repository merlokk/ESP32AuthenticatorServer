using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Persistence;
using AuthServer.Identity.Tests.Devices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AuthServer.IntegrationTests;

// One PostgreSQL container and one host per test class. Tests reset the identity tables first.
public sealed class IdentityFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("authserver")
        .WithUsername("authserver")
        .WithPassword("identity_tests")
        .Build();

    private WebApplicationFactory<Program>? _application;

    public WebApplicationFactory<Program> Application =>
        _application ?? throw new InvalidOperationException("The fixture has not been initialised.");

    public string ConnectionString => _database.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        await TestSchemas.MigrateAsync(ConnectionString);
        _application = CreateApplication();
    }

    public async Task DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    // A second process on the same database: a restart, or a second replica.
    public WebApplicationFactory<Program> CreateSecondApplication(Action<IWebHostBuilder>? extra = null) =>
        CreateApplication(extra);

    private WebApplicationFactory<Program> CreateApplication(Action<IWebHostBuilder>? extra = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            TestHost.Configure(builder, ConnectionString);
            extra?.Invoke(builder);
        });

    public async Task ResetAsync()
    {
        await using var scope = Application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Device clients are OpenIddict applications; the web client is seeded and stays.
        await context.Database.ExecuteSqlRawAsync(
            $"""
            truncate table identity.asp_net_user_claims,
                           identity.asp_net_user_logins,
                           identity.asp_net_user_tokens,
                           identity.asp_net_user_passkeys,
                           identity.devices,
                           identity.asp_net_users,
                           identity.openiddict_tokens,
                           identity.openiddict_authorizations;
            delete from identity.openiddict_applications where client_id <> '{TestHost.ClientId}';
            truncate table sync.store_versions, sync.stores;
            """);
    }

    public HttpClient CreateBrowser(WebApplicationFactory<Program>? application = null) =>
        AccessTokens.CreateBrowser(application ?? Application);

    // A user as the product makes one: an account with an open window, then a device registered
    // through the device API.
    internal async Task<TestAccount> CreateUserAsync(string email, string role = UserRoles.User)
    {
        var id = await CreateAccountAsync(email, role, TimeSpan.FromHours(1));
        var device = new VirtualDevice();

        var registered = await DeviceApi.RegisterAsync(Application.CreateClient(), device, email);
        registered.EnsureSuccessStatusCode();

        return new TestAccount(id, email, device);
    }

    // An account with no device, as an admin's invitation leaves it.
    internal async Task<Guid> CreateAccountAsync(string email, string role = UserRoles.User, TimeSpan? window = null)
    {
        await using var scope = Application.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = AccountFields.DefaultUserNameFor(email) + "-" + Guid.NewGuid().ToString("N")[..6],
            Email = email,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
            RegistrationOpenUntil = window is null ? null : DateTimeOffset.UtcNow + window,
        };

        var created = await users.CreateAsync(user);

        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create the test account: " + string.Join("; ", created.Errors.Select(error => error.Description)));
        }

        return user.Id;
    }

    internal async Task<T> QueryAsync<T>(Func<IdentityDbContext, Task<T>> query)
    {
        await using var scope = Application.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }

    internal Task<ApplicationUser?> FindUserAsync(Guid id) =>
        QueryAsync(context => context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id));
}

internal sealed record TestAccount(Guid Id, string Email, VirtualDevice Device);
