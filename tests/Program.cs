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

ExpectContains("planned maintenance", ConnectionFailureMessages.Dialog(12, null, true, "Ragnavik", "Play"), "Discord");
if (ConnectionFailureMessages.Dialog(12, null, true, "Ragnavik", "Play").Contains("Could not connect"))
    throw new InvalidOperationException("Maintenance must not show the generic failure heading.");
if (!ConnectionFailureMessages.IsMaintenanceReason("[Ragnavik Maintenance] Planned shutdown") ||
    !ConnectionFailureMessages.IsMaintenanceReason("Maintenance countdown finished. Please reconnect after the update.") ||
    ConnectionFailureMessages.IsMaintenanceReason("Authentication failed") ||
    ConnectionFailureMessages.IsMaintenanceReason(null))
    throw new InvalidOperationException("Maintenance reason classification failed.");
ExpectContains("unplanned kick", ConnectionFailureMessages.Dialog(12, null, false, "Ragnavik", "Play"), "rejected");
ExpectContains("Catos takes precedence", ConnectionFailureMessages.Dialog(12, "Connection rejected: wrong mod", true, "Ragnavik", "Play"), "wrong mod");
Console.WriteLine("Maintenance disconnect message tests passed.");
