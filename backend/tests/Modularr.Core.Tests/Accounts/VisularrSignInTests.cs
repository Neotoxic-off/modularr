using Microsoft.EntityFrameworkCore;
using Modularr.Core.Accounts;
using Modularr.Core.Persistence;
using Modularr.Core.Tests.Persistence;

namespace Modularr.Core.Tests.Accounts;

public sealed class VisularrSignInTests : IDisposable
{
    private readonly TestDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    private AccountService Accounts() => new(_database.NewContext());

    // Visularr sign-in was removed: accounts it linked lose their sessions and become plain local accounts; linkarr's stay linked.
    [Fact]
    public async Task VisularrLinkedAccountsAreSignedOutAndUnlinked()
    {
        User alice = await Accounts().CreateAsync("alice", "password1", UserRole.User, Ct);
        User bob = await Accounts().ProvisionAsync("L1", "bob", UserRole.User, "password1", disabled: false, Ct);
        using (ModularrDbContext db = _database.NewContext())
        {
            await db.Users.Where(u => u.Id == alice.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ExternalProvider, "visularr").SetProperty(u => u.ExternalId, "abc"), Ct);
        }

        string aliceSession = await Accounts().StartSessionAsync(alice.Id, Ct);
        string bobSession = await Accounts().StartSessionAsync(bob.Id, Ct);

        Assert.Equal(1, await Accounts().UnlinkVisularrAccountsAsync(Ct));
        Assert.Equal(0, await Accounts().UnlinkVisularrAccountsAsync(Ct));

        Assert.False(await Accounts().IsSessionOpenAsync(aliceSession, alice.Id, Ct));
        Assert.True(await Accounts().IsSessionOpenAsync(bobSession, bob.Id, Ct));
        User? unlinked = await Accounts().FindAsync(alice.Id, Ct);
        Assert.Equal((null, null, false), (unlinked?.ExternalProvider, unlinked?.ExternalId, unlinked?.Disabled));
        Assert.Equal("linkarr", (await Accounts().FindAsync(bob.Id, Ct))?.ExternalProvider);
        // Still a local account with its own password.
        Assert.NotNull(await Accounts().AuthenticateAsync("alice", "password1", Ct));
    }
}
