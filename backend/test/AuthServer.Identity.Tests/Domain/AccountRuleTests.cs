using AuthServer.Identity.Domain;

namespace AuthServer.Identity.Tests.Domain;

public sealed class RegistrationWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Invite = TimeSpan.FromDays(1);
    private static readonly TimeSpan Device = TimeSpan.FromHours(1);

    [Fact]
    public void A_user_without_devices_gets_the_invite_window()
    {
        RegistrationWindow.Until(Now, hasDevices: false, Invite, Device).ShouldBe(Now + Invite);
    }

    [Fact]
    public void A_user_with_a_device_gets_the_device_window()
    {
        RegistrationWindow.Until(Now, hasDevices: true, Invite, Device).ShouldBe(Now + Device);
    }

    [Fact]
    public void A_window_is_open_strictly_before_its_end()
    {
        RegistrationWindow.IsOpen(null, Now).ShouldBeFalse();
        RegistrationWindow.IsOpen(Now.AddSeconds(1), Now).ShouldBeTrue();
        RegistrationWindow.IsOpen(Now, Now).ShouldBeFalse();
        RegistrationWindow.IsOpen(Now.AddSeconds(-1), Now).ShouldBeFalse();
    }
}

public sealed class LastAdminRuleTests
{
    [Theory]
    // target is an active admin, admins in total, target stays an active admin → refused?
    [InlineData(true, 1, false, true)]
    [InlineData(true, 2, false, false)]
    [InlineData(true, 1, true, false)]
    [InlineData(false, 1, false, false)]
    [InlineData(false, 0, false, false)]
    public void Refuses_only_what_would_leave_no_active_admin(
        bool targetIsActiveAdmin, int activeAdmins, bool targetStaysActiveAdmin, bool refused)
    {
        LastAdminRule.Refuses(targetIsActiveAdmin, activeAdmins, targetStaysActiveAdmin).ShouldBe(refused);
    }
}

public sealed class AccountFieldsTests
{
    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("  bob.smith_1-x ", "bob.smith_1-x")]
    public void A_user_name_is_trimmed(string input, string expected)
    {
        AccountFields.UserNameOf(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("ünïcode")]
    [InlineData("a@b")]
    public void A_user_name_outside_the_alphabet_is_refused(string? input)
    {
        AccountFields.UserNameOf(input).Error.ShouldBe(IdentityErrors.UserNameInvalid);
    }

    [Fact]
    public void A_user_name_is_at_most_64_characters()
    {
        AccountFields.UserNameOf(new string('a', 64)).IsSuccess.ShouldBeTrue();
        AccountFields.UserNameOf(new string('a', 65)).Error.ShouldBe(IdentityErrors.UserNameInvalid);
    }

    [Theory]
    [InlineData(" Alice@Example.com ", "Alice@Example.com")]
    [InlineData("a@b.c", "a@b.c")]
    public void An_email_is_trimmed_and_otherwise_kept(string input, string expected)
    {
        AccountFields.EmailOf(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("Name <a@b.c>")]
    [InlineData("a@b\n.c")]
    public void A_bare_address_is_required(string? input)
    {
        AccountFields.EmailOf(input).Error.ShouldBe(IdentityErrors.EmailInvalid);
    }

    [Theory]
    [InlineData("alice@example.com", "alice")]
    [InlineData("first.last+tag@example.com", "first.last-tag")]
    [InlineData("@example.com", "user")]
    public void The_default_user_name_is_the_local_part_within_the_alphabet(string email, string expected)
    {
        AccountFields.DefaultUserNameFor(email).ShouldBe(expected);
    }

    [Theory]
    [InlineData("admin", "admin")]
    [InlineData("USER", "user")]
    public void A_role_is_one_of_two(string input, string expected)
    {
        UserRoles.Of(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("owner")]
    public void Any_other_role_is_none(string? input)
    {
        UserRoles.Of(input).ShouldBeNull();
    }
}
