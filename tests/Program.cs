using RagnavikUI;

static void ExpectContains(string name, string actual, string expected)
{
    if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"{name}: expected '{expected}' in '{actual}'");
}

ExpectContains("game version", ConnectionFailureMessages.Format(3, null), "game or network version");
ExpectContains("transport", ConnectionFailureMessages.Format(5, null), "could not be reached");
ExpectContains("authentication", ConnectionFailureMessages.Format(6, null), "password");
ExpectContains("rejection", ConnectionFailureMessages.Format(12, null), "rejected");
ExpectContains("unknown", ConnectionFailureMessages.Format(99, null), "unknown reason");
ExpectContains("Catos category", ConnectionFailureMessages.Format(4, "Connection rejected: example.mod version mismatch"), "mod setup was rejected");
ExpectContains("Catos detail", ConnectionFailureMessages.Format(4, "Connection rejected: example.mod version mismatch"), "example.mod version mismatch");

Console.WriteLine("Connection failure message tests passed.");

var status = new ServerConnectionStatus { schemaVersion = 1, state = "online",
    requiredClientVersion = "1.1.60", checkedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
ExpectContains("active version", ConnectionFailureMessages.Format(4, "Connection rejected: mismatch", status), "1.1.60");
ExpectContains("newer client", ConnectionFailureMessages.Format(4, "Connection rejected: mismatch", status), "If your pack is newer");
status.state = "maintenance";
ExpectContains("maintenance", ConnectionFailureMessages.Format(5, null, status), "undergoing maintenance");
status.state = "unavailable";
ExpectContains("offline", ConnectionFailureMessages.Format(5, null, status), "not ready");
status.checkedAt -= 300;
ExpectContains("stale", ConnectionFailureMessages.Format(5, null, status), "could not be verified");
status.checkedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
if (status.IsFresh(DateTimeOffset.UtcNow)) throw new Exception("Future status accepted");
status.requiredClientVersion = "<b>1.2.3</b>";
if (status.RequiredVersion != null) throw new Exception("Invalid version accepted");
ExpectContains("escape rejection", ConnectionFailureMessages.Format(4, "Connection rejected: <size=100>mod"), "‹size=100›mod");
Console.WriteLine("Server status and version guidance tests passed.");

status.state = "maintenance";
status.checkedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
ExpectContains("password preserved", ConnectionFailureMessages.Format(6, null, status), "password was not accepted");

status.requiredClientVersion = null;
status.lastVerifiedClientVersion = "1.1.60";
status.state = "unavailable";
ExpectContains("last verified version", ConnectionFailureMessages.Format(5, null, status), "Last verified Ragnavik client pack: 1.1.60");
ExpectContains("last version is not current", ConnectionFailureMessages.Format(5, null, status), "current server requirement is not confirmed");
