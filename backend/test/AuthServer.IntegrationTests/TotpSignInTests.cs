using System.Net;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Tests.Devices;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.IntegrationTests;

// UC-9: e-mail + the TOTP code a device shows.
public sealed class TotpSignInTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_current_code_signs_in_without_a_token_in_the_answer_and_stamps_the_sign_in()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var browser = fixture.CreateBrowser();

        var response = await AccessTokens.SignInWithTotpAsync(browser, "USER@example.com", account.Device.Code(DateTimeOffset.UtcNow));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("eyJ");
        response.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        (await fixture.FindUserAsync(account.Id))!.LastSignInAt.ShouldNotBeNull();

        var (accessToken, _) = await AccessTokens.ObtainFromCookieAsync(browser);
        AccessTokens.Read(accessToken).Subject.ShouldBe(account.Id.ToString());
    }

    [Fact]
    public async Task A_code_is_accepted_once()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var code = account.Device.Code(DateTimeOffset.UtcNow);

        (await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, code)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var replay = await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, code);
        await replay.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "sign-in.refused");
    }

    [Fact]
    public async Task Any_of_the_users_devices_signs_in()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        await fixture.QueryAsync(context => context.Users.Where(user => user.Id == account.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.RegistrationOpenUntil, DateTimeOffset.UtcNow.AddHours(1))));
        using var second = new VirtualDevice();
        (await DeviceApi.RegisterAsync(fixture.Application.CreateClient(), second, account.Email)).EnsureSuccessStatusCode();

        var response = await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, second.Code(DateTimeOffset.UtcNow));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Every_cause_produces_one_identical_refusal()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var blocked = await fixture.CreateUserAsync("blocked@example.com");
        await fixture.QueryAsync(context => context.Users.Where(user => user.Id == blocked.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.BlockedAt, DateTimeOffset.UtcNow)));
        var deviceless = await fixture.CreateAccountAsync("nodevice@example.com");
        var now = DateTimeOffset.UtcNow;

        var refusals = new[]
        {
            await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), "nobody@example.com", "123456"),
            await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, WrongCode(account.Device, now)),
            await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, ""),
            await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), blocked.Email, blocked.Device.Code(now)),
            await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), "nodevice@example.com", "123456"),
        };

        var bodies = new List<string>();

        foreach (var refusal in refusals)
        {
            await refusal.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "sign-in.refused");
            refusal.Headers.Contains("Set-Cookie").ShouldBeFalse();
            bodies.Add(await refusal.Content.ReadAsStringAsync());
        }

        bodies.Distinct().Count().ShouldBe(1);
        deviceless.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_account_and_then_a_right_code_is_refused_too()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, WrongCode(account.Device, now)))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var locked = await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, account.Device.Code(now));

        await locked.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "sign-in.refused");
    }

    [Fact]
    public async Task A_right_code_resets_the_failure_count()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, WrongCode(account.Device, now));
        }

        (await AccessTokens.SignInWithTotpAsync(fixture.CreateBrowser(), account.Email, account.Device.Code(now)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await fixture.FindUserAsync(account.Id))!.AccessFailedCount.ShouldBe(0);
    }

    // A code that matches none of the three acceptable steps.
    private static string WrongCode(VirtualDevice device, DateTimeOffset now)
    {
        var step = Totp.StepAt(now);
        var valid = new[] { -1, 0, 1 }.Select(offset => Totp.Code(device.TotpSecret, step + offset)).ToHashSet();

        return Enumerable.Range(0, 1000).Select(value => value.ToString("D6")).First(code => !valid.Contains(code));
    }
}
