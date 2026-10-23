using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modularr.Core.Persistence;
using Modularr.Core.Search;

namespace Modularr.Core.Accounts;

// Local accounts, every one created by linkarr's provisioning (there is no first-run setup); older local ones can be adopted.
public sealed partial class AccountService(ModularrDbContext db)
{
    public const int MinPasswordLength = 8;

    // ExternalProvider of the accounts linkarr provisions.
    public const string LinkarrProvider = "linkarr";

    private static readonly PasswordHasher<User> Hasher = new();

    // Verified against for an unknown username, so a wrong name costs the same time as a wrong password.
    private static readonly string DummyHash = Hasher.HashPassword(new User { Username = "-", PasswordHash = "" }, "not a password");

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public async Task<User?> FindAsync(int id, CancellationToken cancellationToken) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<User> CreateAsync(string username, string password, UserRole role, CancellationToken cancellationToken)
    {
        await ValidateUsernameAsync(username, exceptId: null, cancellationToken).ConfigureAwait(false);
        ValidatePassword(password, nameof(password));
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException("Role must be Admin or User.", nameof(role));
        }

        User user = new() { Username = username, PasswordHash = string.Empty, Role = role, Created = DateTime.UtcNow };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }

    // Null for an unknown username or a wrong password.
    public async Task<User?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        User? user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && !u.Disabled, cancellationToken).ConfigureAwait(false);
        if (user is null || password is null)
        {
            Hasher.VerifyHashedPassword(new User { Username = "-", PasswordHash = DummyHash }, DummyHash, password ?? "");
            return null;
        }

        switch (Hasher.VerifyHashedPassword(user, user.PasswordHash, password))
        {
            case PasswordVerificationResult.Success:
                return user;
            case PasswordVerificationResult.SuccessRehashNeeded:
                user.PasswordHash = Hasher.HashPassword(user, password);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return user;
            default:
                return null;
        }
    }

    // Visularr sign-in is gone: the accounts it linked keep their random local password (linkarr can adopt them by name), but their
    // sessions end, so nobody keeps access through it. The provider is cleared so this runs once per account.
    public async Task<int> UnlinkVisularrAccountsAsync(CancellationToken cancellationToken)
    {
        List<int> ids = await db.Users.Where(u => u.ExternalProvider == "visularr").Select(u => u.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Count == 0)
        {
            return 0;
        }

        await db.Sessions.Where(s => ids.Contains(s.UserId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Users.Where(u => ids.Contains(u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExternalProvider, (string?)null).SetProperty(u => u.ExternalId, (string?)null), cancellationToken)
            .ConfigureAwait(false);
        return ids.Count;
    }

    public Task<User> ProvisionAsync(string externalId, string username, UserRole role, string? password, bool disabled, CancellationToken cancellationToken) =>
        ProvisionAsync(externalId, username, role, password, disabled, adopt: false, cancellationToken);

    // linkarr's provisioning: the account linked to its user id, created or brought in line with it. A local account (never
    // linked) with the same name is adopted only when linkarr's admin explicitly adopts it (adopt); otherwise, like a name held
    // by another linked account, it is refused. A role change, a new password or disabling ends the account's sessions.
    public async Task<User> ProvisionAsync(string externalId, string username, UserRole role, string? password, bool disabled, bool adopt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        User? user = await db.Users.FirstOrDefaultAsync(u => u.ExternalProvider == LinkarrProvider && u.ExternalId == externalId, cancellationToken)
                .ConfigureAwait(false)
            ?? (adopt ? await db.Users.FirstOrDefaultAsync(u => u.ExternalProvider == null && u.Username == username, cancellationToken).ConfigureAwait(false) : null);
        if (user is not null && user.ExternalProvider is null)
        {
            user.ExternalProvider = LinkarrProvider;
            user.ExternalId = externalId;
        }

        int? id = user?.Id;
        if (await db.Users.AnyAsync(u => u.Username == username && u.Id != id, cancellationToken).ConfigureAwait(false))
        {
            throw new AlreadyPresentException($"Username '{username}' belongs to another account.");
        }

        await ValidateUsernameAsync(username, id, cancellationToken).ConfigureAwait(false);
        if (password is not null)
        {
            ValidatePassword(password, nameof(password));
        }

        if (user is null)
        {
            if (!Enum.IsDefined(role))
            {
                throw new ArgumentException("Role must be Admin or User.", nameof(role));
            }

            // Without a password the account has no usable local one until linkarr sends it.
            user = new User
            {
                Username = username, PasswordHash = string.Empty, Role = role, Created = DateTime.UtcNow,
                ExternalProvider = LinkarrProvider, ExternalId = externalId, Disabled = disabled,
            };
            user.PasswordHash = Hasher.HashPassword(user, password ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return user;
        }

        if (disabled && !user.Disabled)
        {
            await EnsureNotLastAdminAsync(user, "disabled", cancellationToken).ConfigureAwait(false);
        }

        user.Username = username;
        user.Disabled = disabled;
        user = await UpdateAsync(user.Id, role, password, cancellationToken).ConfigureAwait(false);
        if (disabled)
        {
            await db.Sessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        return user;
    }

    // Unknown ids are already gone; the last admin is refused like any delete.
    public async Task DeprovisionAsync(string externalId, CancellationToken cancellationToken)
    {
        User? user = await db.Users.FirstOrDefaultAsync(u => u.ExternalProvider == LinkarrProvider && u.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return;
        }

        await EnsureNotLastAdminAsync(user, "deleted", cancellationToken).ConfigureAwait(false);
        db.Users.Remove(user); // its sessions go with it (cascade)
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public const int MaxAvatarBytes = 512 * 1024;

    // Only raster formats recognised by their first bytes: never SVG or HTML, which the browser would run as this origin.
    public static string? AvatarType(ReadOnlySpan<byte> image) =>
        image.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? "image/png"
        : image.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ? "image/jpeg"
        : image.StartsWith("GIF87a"u8) || image.StartsWith("GIF89a"u8) ? "image/gif"
        : image.Length >= 12 && image.StartsWith("RIFF"u8) && image[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;

    public async Task<User> SetAvatarAsync(int id, byte[] image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Length > MaxAvatarBytes)
        {
            throw new ArgumentException($"The picture must be at most {MaxAvatarBytes / 1024} KB.", nameof(image));
        }

        string type = AvatarType(image) ?? throw new ArgumentException("The picture must be a PNG, JPEG, GIF or WebP image.", nameof(image));
        User user = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        UserAvatar? avatar = await db.UserAvatars.FirstOrDefaultAsync(a => a.UserId == id, cancellationToken).ConfigureAwait(false);
        if (avatar is null)
        {
            db.UserAvatars.Add(new UserAvatar { UserId = id, Content = image, ContentType = type });
        }
        else
        {
            avatar.Content = image;
            avatar.ContentType = type;
        }

        user.AvatarUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }

    public async Task<User> RemoveAvatarAsync(int id, CancellationToken cancellationToken)
    {
        User user = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        await db.UserAvatars.Where(a => a.UserId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        user.AvatarUpdated = null;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }

    public async Task<UserAvatar?> FindAvatarAsync(int id, CancellationToken cancellationToken) =>
        await db.UserAvatars.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == id, cancellationToken).ConfigureAwait(false);

    private async Task ValidateUsernameAsync(string username, int? exceptId, CancellationToken cancellationToken)
    {
        if (username is null || !UsernamePattern().IsMatch(username))
        {
            throw new ArgumentException("Username must be 3 to 32 characters among letters, digits, '.', '_' and '-'.", nameof(username));
        }

        if (await db.Users.AnyAsync(u => u.Username == username && u.Id != exceptId, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"Username '{username}' is already taken.", nameof(username));
        }
    }

    public async Task<User> UpdateAsync(int id, UserRole? role, string? password, CancellationToken cancellationToken)
    {
        User user = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (role is UserRole newRole && newRole != user.Role)
        {
            if (!Enum.IsDefined(newRole))
            {
                throw new ArgumentException("Role must be Admin or User.", nameof(role));
            }

            await EnsureNotLastAdminAsync(user, "demoted", cancellationToken).ConfigureAwait(false);
            user.Role = newRole;
            // The stamp alone would come back if the old role were restored, reviving every cookie the change revoked.
            await db.Sessions.Where(s => s.UserId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        if (password is not null)
        {
            ValidatePassword(password, nameof(password));
            user.PasswordHash = Hasher.HashPassword(user, password);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }

    // A new signed-in browser; sessions idle for SessionLifetime are dropped on the way.
    public async Task<string> StartSessionAsync(int userId, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        await db.Sessions.Where(s => s.LastSeen < now - SessionLifetime).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.Sessions.Add(new Session { Id = id, UserId = userId, Created = now, LastSeen = now });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    // Whether the session is still open (not logged out, not ended by a password change).
    public async Task<bool> IsSessionOpenAsync(string sessionId, int userId, CancellationToken cancellationToken)
    {
        Session? session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return false;
        }

        if (session.LastSeen < DateTime.UtcNow.AddDays(-1))
        {
            session.LastSeen = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task EndSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

    // "Sign out other devices": only the browser asking stays signed in.
    public async Task EndOtherSessionsAsync(int userId, string keepSessionId, CancellationToken cancellationToken) =>
        await db.Sessions.Where(s => s.UserId == userId && s.Id != keepSessionId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

    private async Task<User> GetAsync(int id, CancellationToken cancellationToken) =>
        await FindAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException($"User {id} does not exist.");

    private async Task EnsureNotLastAdminAsync(User user, string action, CancellationToken cancellationToken)
    {
        // A disabled admin cannot sign in, so it does not count as the one left.
        if (user.Role == UserRole.Admin
            && !await db.Users.AnyAsync(u => u.Role == UserRole.Admin && !u.Disabled && u.Id != user.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"The last admin cannot be {action}.", nameof(user));
        }
    }

    private static void ValidatePassword(string password, string paramName)
    {
        if (password is null || password.Length < MinPasswordLength)
        {
            throw new ArgumentException($"Password must be at least {MinPasswordLength} characters.", paramName);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{3,32}$")]
    private static partial Regex UsernamePattern();
}
