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
