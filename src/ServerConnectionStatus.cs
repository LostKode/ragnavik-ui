using System;

namespace RagnavikUI;

// Public status contains no server address, credentials, or player data.
[Serializable]
internal sealed class ServerConnectionStatus
{
    public int schemaVersion;
    public string? state;
    public string? requiredClientVersion;
    public string? lastVerifiedClientVersion;
    public long checkedAt;

    internal bool IsFresh(DateTimeOffset now) => schemaVersion == 1 &&
        checkedAt <= now.ToUnixTimeSeconds() + 30 &&
        checkedAt >= now.ToUnixTimeSeconds() - 120 &&
        state is "online" or "maintenance" or "unavailable" or "unknown";

    internal string? RequiredVersion => CleanVersion(requiredClientVersion);
    internal string? LastVerifiedVersion => CleanVersion(lastVerifiedClientVersion);
    private static string? CleanVersion(string? version) => Version.TryParse(version, out var value) &&
        value.Build >= 0 && value.Revision == -1 ? value.ToString() : null;
}
