using System;
using System.Collections.Concurrent;

namespace FOMS.Application.Services;

/// <summary>
/// SessionActivityTracker (IAM-02): Tracks session activity timestamps for 15-minute sliding inactivity expiration.
/// </summary>
public static class SessionActivityTracker
{
    private static readonly ConcurrentDictionary<string, DateTime> _lastActivity = new();

    /// <summary>
    /// Checks activity for the session token hash. If elapsed time exceeds timeout, returns true and purges session.
    /// Otherwise updates last activity timestamp to UtcNow (sliding expiry).
    /// </summary>
    public static (bool isExpired, TimeSpan elapsed) CheckAndUpdateActivity(string tokenHash, TimeSpan timeout)
    {
        var now = DateTime.UtcNow;
        if (_lastActivity.TryGetValue(tokenHash, out var last))
        {
            var elapsed = now - last;
            if (elapsed > timeout)
            {
                _lastActivity.TryRemove(tokenHash, out _);
                return (true, elapsed);
            }
        }
        _lastActivity[tokenHash] = now;
        return (false, TimeSpan.Zero);
    }

    /// <summary>
    /// Invalidates session activity on explicit logout.
    /// </summary>
    public static void Invalidate(string tokenHash) => _lastActivity.TryRemove(tokenHash, out _);

    /// <summary>
    /// For unit/integration test simulation of inactivity passage.
    /// </summary>
    public static void SimulateLastActivity(string tokenHash, DateTime timestamp) => _lastActivity[tokenHash] = timestamp;

    /// <summary>
    /// Clears all tracked sessions (e.g., during test resets).
    /// </summary>
    public static void Clear() => _lastActivity.Clear();
}
