namespace Modularr.Core.Persistence;

// One signed-in browser. Its cookie carries the Id; logging out deletes the row, which ends that cookie at once even if a copy
// of it exists elsewhere.
public sealed class Session
{
    public required string Id { get; set; }
    public int UserId { get; set; }
    public DateTime Created { get; set; }

    // Refreshed at most daily; a session unused for SessionLifetime is pruned (the cookie itself expires after 30 idle days).
    public DateTime LastSeen { get; set; }
}
