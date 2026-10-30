namespace Modularr.Core.Persistence;

public enum UserRole
{
    Admin = 0,
    User = 1,
}

public sealed class User
{
    public int Id { get; set; }

    // Unique, compared case-insensitively.
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public DateTime Created { get; set; }

    // Every activity up to this id is read for this user; each user keeps their own unread count.
    public int ReadActivityUpTo { get; set; }

    // When the picture last changed (its URL carries it, so browsers refetch), null without one.
    public DateTime? AvatarUpdated { get; set; }

    // An account provisioned by another server ("linkarr") and its user id there; null for a local account.
    public string? ExternalProvider { get; set; }
    public string? ExternalId { get; set; }

    // Set by linkarr's provisioning: the account cannot sign in and keeps no session.
    public bool Disabled { get; set; }
}
