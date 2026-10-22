using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modularr.Core.Persistence;

namespace Modularr.Core.Accounts;

// Local accounts: the first one is created by setup as admin, the others by an admin.
public sealed partial class AccountService(ModularrDbContext db)
{
    public const int MinPasswordLength = 8;

    private static readonly PasswordHasher<User> Hasher = new();

    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken) =>
        !await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false);

    public async Task<User?> FindAsync(int id, CancellationToken cancellationToken) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancellationToken).ConfigureAwait(false);

    // Null once any account exists: setup only ever creates the first admin.
    public async Task<User?> SetupAsync(string username, string password, CancellationToken cancellationToken) =>
        await IsSetupRequiredAsync(cancellationToken).ConfigureAwait(false)
            ? await CreateAsync(username, password, UserRole.Admin, cancellationToken).ConfigureAwait(false)
            : null;

    public async Task<User> CreateAsync(string username, string password, UserRole role, CancellationToken cancellationToken)
    {
        if (username is null || !UsernamePattern().IsMatch(username))
        {
            throw new ArgumentException("Username must be 3 to 32 characters among letters, digits, '.', '_' and '-'.", nameof(username));
        }

        ValidatePassword(password, nameof(password));
        if (await db.Users.AnyAsync(u => u.Username == username, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"Username '{username}' is already taken.", nameof(username));
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
        User? user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken).ConfigureAwait(false);
        if (user is null || password is null)
        {
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

    public async Task<User> ChangePasswordAsync(int id, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        User user = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (currentPassword is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            throw new ArgumentException("The current password is wrong.", nameof(currentPassword));
        }

        return await UpdateAsync(id, role: null, newPassword, cancellationToken).ConfigureAwait(false);
    }

    public async Task<User> UpdateAsync(int id, UserRole? role, string? password, CancellationToken cancellationToken)
    {
        User user = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (role is UserRole newRole && newRole != user.Role)
        {
            await EnsureNotLastAdminAsync(user, "demoted", cancellationToken).ConfigureAwait(false);
            user.Role = newRole;
        }

        if (password is not null)
        {
            ValidatePassword(password, nameof(password));
            user.PasswordHash = Hasher.HashPassword(user, password);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }

    public async Task DeleteAsync(int id, int currentUserId, CancellationToken cancellationToken)
    {
        if (id == currentUserId)
        {
            throw new ArgumentException("You cannot delete your own account.", nameof(id));
        }

        User user = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        await EnsureNotLastAdminAsync(user, "deleted", cancellationToken).ConfigureAwait(false);
        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<User> GetAsync(int id, CancellationToken cancellationToken) =>
        await FindAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException($"User {id} does not exist.");

    private async Task EnsureNotLastAdminAsync(User user, string action, CancellationToken cancellationToken)
    {
        if (user.Role == UserRole.Admin && await db.Users.CountAsync(u => u.Role == UserRole.Admin, cancellationToken).ConfigureAwait(false) == 1)
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
