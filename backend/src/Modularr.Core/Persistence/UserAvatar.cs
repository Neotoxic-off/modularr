namespace Modularr.Core.Persistence;

// Kept apart from User, which every request loads to check its cookie.
public sealed class UserAvatar
{
    public int UserId { get; set; }
    public required byte[] Content { get; set; }
    public required string ContentType { get; set; }
}
