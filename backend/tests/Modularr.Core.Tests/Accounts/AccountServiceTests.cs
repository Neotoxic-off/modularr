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
    public async Task SetupCreatesTheFirstAdminOnlyOnce()
    {
        Assert.True(await Accounts().IsSetupRequiredAsync(Ct));

        User? admin = await Accounts().SetupAsync("admin", "password1", Ct);

        Assert.Equal(UserRole.Admin, admin?.Role);
        Assert.False(await Accounts().IsSetupRequiredAsync(Ct));
        Assert.Null(await Accounts().SetupAsync("other", "password2", Ct));
    }

    [Fact]
    public async Task AuthenticatesCaseInsensitivelyByUsernameOnly()
    {
        await Accounts().SetupAsync("Admin", "password1", Ct);

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
        User admin = (await Accounts().SetupAsync("admin", "password1", Ct))!;
        User other = await Accounts().CreateAsync("other", "password1", UserRole.User, Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().UpdateAsync(admin.Id, UserRole.User, null, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().DeleteAsync(admin.Id, other.Id, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().DeleteAsync(admin.Id, admin.Id, Ct));

        await Accounts().UpdateAsync(other.Id, UserRole.Admin, null, Ct);
        await Accounts().UpdateAsync(admin.Id, UserRole.User, null, Ct);
        await Accounts().DeleteAsync(admin.Id, other.Id, Ct);
        Assert.Equal(["other"], (await Accounts().ListAsync(Ct)).Select(u => u.Username));
    }

    [Fact]
    public async Task ChangesThePasswordOnlyWithTheCurrentOne()
    {
        User admin = (await Accounts().SetupAsync("admin", "password1", Ct))!;

        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().ChangePasswordAsync(admin.Id, "wrong-password", "password2", Ct));
        await Accounts().ChangePasswordAsync(admin.Id, "password1", "password2", Ct);

        Assert.Null(await Accounts().AuthenticateAsync("admin", "password1", Ct));
        Assert.NotNull(await Accounts().AuthenticateAsync("admin", "password2", Ct));
    }

    [Fact]
    public async Task UnknownUserIsNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Accounts().UpdateAsync(42, null, "password1", Ct));
    }
}
