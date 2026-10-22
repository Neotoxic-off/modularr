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
}
