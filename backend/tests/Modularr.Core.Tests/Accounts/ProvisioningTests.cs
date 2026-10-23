using Modularr.Core.Accounts;
using Modularr.Core.Persistence;
using Modularr.Core.Search;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Accounts;

public sealed class ProvisioningTests : IDisposable
{
    private readonly TestDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    private AccountService Accounts() => new(_database.NewContext());

    [Fact]
    public async Task ProvisioningCreatesThenUpdatesTheLinkedAccount()
    {
        await Accounts().CreateAsync("owner", "password1", UserRole.Admin, Ct);

        User created = await Accounts().ProvisionAsync("L1", "alice", UserRole.User, "password1", disabled: false, Ct);
        User updated = await Accounts().ProvisionAsync("L1", "alice2", UserRole.User, "password2", disabled: false, Ct);

        Assert.Equal(("linkarr", "L1"), (created.ExternalProvider, created.ExternalId));
        Assert.Equal((created.Id, "alice2"), (updated.Id, updated.Username));
        Assert.Null(await Accounts().AuthenticateAsync("alice2", "password1", Ct));
        Assert.Equal(created.Id, (await Accounts().AuthenticateAsync("alice2", "password2", Ct))?.Id);
        Assert.Equal(2, (await Accounts().ListAsync(Ct)).Count);
    }

    [Fact]
    public async Task ANameOfAnotherAccountIsRefused()
    {
        await Accounts().CreateAsync("owner", "password1", UserRole.Admin, Ct);
        await Accounts().ProvisionAsync("L1", "alice", UserRole.User, null, disabled: false, Ct);

        await Assert.ThrowsAsync<AlreadyPresentException>(() => Accounts().ProvisionAsync("L2", "alice", UserRole.User, null, disabled: false, Ct));
        await Assert.ThrowsAsync<AlreadyPresentException>(() => Accounts().ProvisionAsync("L2", "ALICE", UserRole.User, null, disabled: false, Ct));
    }

    [Fact]
    public async Task ANeverLinkedLocalAccountIsAdopted()
    {
        User owner = await Accounts().CreateAsync("owner", "password1", UserRole.Admin, Ct);
        // Plain provisioning never takes over a local account by its name: only linkarr's explicit adopt does.
        await Assert.ThrowsAsync<AlreadyPresentException>(() => Accounts().ProvisionAsync("L8", "owner", UserRole.Admin, "password2", disabled: false, Ct));

        User adopted = await Accounts().ProvisionAsync("L9", "OWNER", UserRole.Admin, null, disabled: false, adopt: true, Ct);

        Assert.Equal((owner.Id, "linkarr", "L9"), (adopted.Id, adopted.ExternalProvider, adopted.ExternalId));
        Assert.Single(await Accounts().ListAsync(Ct));
        // Still the last admin: adopting does not let linkarr demote it.
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().ProvisionAsync("L9", "owner", UserRole.User, null, disabled: false, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().ProvisionAsync("L2", "no spaces", UserRole.User, null, disabled: false, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().ProvisionAsync("L2", "bob", UserRole.User, "short", disabled: false, Ct));
    }

    [Fact]
    public async Task ARoleChangeEndsTheSessions()
    {
        await Accounts().CreateAsync("owner", "password1", UserRole.Admin, Ct);
        User user = await Accounts().ProvisionAsync("L1", "alice", UserRole.User, "password1", disabled: false, Ct);
        string session = await Accounts().StartSessionAsync(user.Id, Ct);

        await Accounts().ProvisionAsync("L1", "alice", UserRole.User, null, disabled: false, Ct);
        Assert.True(await Accounts().IsSessionOpenAsync(session, user.Id, Ct));

        User promoted = await Accounts().ProvisionAsync("L1", "alice", UserRole.Admin, null, disabled: false, Ct);
        Assert.Equal(UserRole.Admin, promoted.Role);
        Assert.False(await Accounts().IsSessionOpenAsync(session, user.Id, Ct));
    }

    [Fact]
    public async Task ADisabledAccountCannotSignInAndLosesItsSessions()
    {
        await Accounts().CreateAsync("owner", "password1", UserRole.Admin, Ct);
        User user = await Accounts().ProvisionAsync("L1", "alice", UserRole.User, "password1", disabled: false, Ct);
        string session = await Accounts().StartSessionAsync(user.Id, Ct);

        await Accounts().ProvisionAsync("L1", "alice", UserRole.User, null, disabled: true, Ct);

        Assert.False(await Accounts().IsSessionOpenAsync(session, user.Id, Ct));
        Assert.Null(await Accounts().AuthenticateAsync("alice", "password1", Ct));
        await Accounts().ProvisionAsync("L1", "alice", UserRole.User, null, disabled: false, Ct);
        Assert.NotNull(await Accounts().AuthenticateAsync("alice", "password1", Ct));
    }

    [Fact]
    public async Task DeprovisioningRemovesTheAccountButNeverTheLastAdmin()
    {
        User admin = await Accounts().ProvisionAsync("A1", "admin", UserRole.Admin, "password1", disabled: false, Ct);
        User member = await Accounts().ProvisionAsync("M1", "member", UserRole.User, "password1", disabled: false, Ct);
        await Accounts().StartSessionAsync(member.Id, Ct);

        await Accounts().DeprovisionAsync("M1", Ct);
        await Accounts().DeprovisionAsync("unknown", Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().DeprovisionAsync("A1", Ct));

        Assert.Equal([admin.Id], (await Accounts().ListAsync(Ct)).Select(u => u.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => Accounts().ProvisionAsync("A1", "admin", UserRole.User, null, disabled: false, Ct));
    }
}
