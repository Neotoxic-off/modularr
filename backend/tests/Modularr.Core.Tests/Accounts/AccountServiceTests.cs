using Modularr.Core.Accounts;
using Modularr.Core.Persistence;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Accounts;

public sealed class AccountServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    private AccountService Accounts() => new(_database.NewContext());

    [Fact]
    public async Task LoggingOutEndsOnlyThatSession()
    {
        // Run-2 audit: a copied cookie stayed valid after logout.
        User admin = await Accounts().CreateAsync("admin", "password1", UserRole.Admin, Ct);
        string phone = await Accounts().StartSessionAsync(admin.Id, Ct);
        string laptop = await Accounts().StartSessionAsync(admin.Id, Ct);

        await Accounts().EndSessionAsync(phone, Ct);

        Assert.False(await Accounts().IsSessionOpenAsync(phone, admin.Id, Ct));
        Assert.True(await Accounts().IsSessionOpenAsync(laptop, admin.Id, Ct));
        Assert.False(await Accounts().IsSessionOpenAsync(laptop, admin.Id + 1, Ct));
    }

    [Fact]
    public async Task ARoleChangeEndsTheUsersSessionsForGood()
    {
        // Run-3 audit: restoring the old role restored the old stamp, reviving the cookies the demotion had revoked.
        await Accounts().CreateAsync("admin", "password1", UserRole.Admin, Ct);
        User bob = await Accounts().CreateAsync("bob", "password1", UserRole.Admin, Ct);
        string session = await Accounts().StartSessionAsync(bob.Id, Ct);

        await Accounts().UpdateAsync(bob.Id, UserRole.User, null, Ct);
        await Accounts().UpdateAsync(bob.Id, UserRole.Admin, null, Ct);

        Assert.False(await Accounts().IsSessionOpenAsync(session, bob.Id, Ct));
    }

    [Fact]
    public async Task CreatingAUserNeedsAKnownRole() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().CreateAsync("someone", "password1", (UserRole)7, Ct));

    [Fact]
    public async Task AnAvatarMustBeARasterImageWithinTheLimit()
    {
        User admin = await Accounts().CreateAsync("admin", "password1", UserRole.Admin, Ct);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        // SVG or HTML would run as this origin when opened directly.
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().SetAvatarAsync(admin.Id, "<svg onload=alert(1)>"u8.ToArray(), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().SetAvatarAsync(admin.Id, [.. png, .. new byte[AccountService.MaxAvatarBytes]], Ct));

        Assert.NotNull((await Accounts().SetAvatarAsync(admin.Id, png, Ct)).AvatarUpdated);
        Assert.Equal("image/png", (await Accounts().FindAvatarAsync(admin.Id, Ct))?.ContentType);
        Assert.Null((await Accounts().RemoveAvatarAsync(admin.Id, Ct)).AvatarUpdated);
        Assert.Null(await Accounts().FindAvatarAsync(admin.Id, Ct));
    }

    [Fact]
    public async Task AuthenticatesCaseInsensitivelyByUsernameOnly()
    {
        await Accounts().CreateAsync("Admin", "password1", UserRole.Admin, Ct);

        Assert.NotNull(await Accounts().AuthenticateAsync("admin", "password1", Ct));
        Assert.Null(await Accounts().AuthenticateAsync("admin", "Password1", Ct));
        Assert.Null(await Accounts().AuthenticateAsync("nobody", "password1", Ct));
    }

    [Theory]
    [InlineData("ab", "password1")]
    [InlineData("has space", "password1")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", "password1")]
    [InlineData("valid.name-1_", "short")]
    public async Task RejectsInvalidUsernamesAndPasswords(string username, string password)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().CreateAsync(username, password, UserRole.User, Ct));
    }

    [Fact]
    public async Task RejectsADuplicateUsernameInAnyCase()
    {
        await Accounts().CreateAsync("alice", "password1", UserRole.User, Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().CreateAsync("ALICE", "password1", UserRole.User, Ct));
    }

    [Fact]
    public async Task KeepsTheLastAdmin()
    {
        User admin = await Accounts().CreateAsync("admin", "password1", UserRole.Admin, Ct);
        User other = await Accounts().CreateAsync("other", "password1", UserRole.User, Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().UpdateAsync(admin.Id, UserRole.User, null, Ct));

        await Accounts().UpdateAsync(other.Id, UserRole.Admin, null, Ct);
        await Accounts().UpdateAsync(admin.Id, UserRole.User, null, Ct);
        Assert.Equal([UserRole.User, UserRole.Admin], (await Accounts().ListAsync(Ct)).Select(u => u.Role));
    }

    [Fact]
    public async Task UnknownUserIsNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Accounts().UpdateAsync(42, null, "password1", Ct));
    }
}
