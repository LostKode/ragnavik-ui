using System;
using System.Collections.Generic;
using System.IO;

namespace RagnavikUI;

internal enum RagnavikCharacterEnvironment
{
    Production,
    Test,
}

internal static class RagnavikCharacterEnvironmentSource
{
    internal const string EnvironmentToken = "__RAGNAVIK_ENTRY_ENVIRONMENT__";

    internal static RagnavikCharacterEnvironment Load(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Character environment file is missing.");
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            int separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1) throw new InvalidOperationException("Character environment file is invalid.");
            if (!values.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim())) throw new InvalidOperationException("Character environment file contains duplicate keys.");
        }
        if (values.Count != 1 || !values.TryGetValue("Environment", out string? value)) throw new InvalidOperationException("Character environment file is incomplete.");
        if (value.Contains(EnvironmentToken, StringComparison.Ordinal)) throw new InvalidOperationException("Character environment build token was not injected.");
        if (value.Equals("Production", StringComparison.OrdinalIgnoreCase)) return RagnavikCharacterEnvironment.Production;
        if (value.Equals("Test", StringComparison.OrdinalIgnoreCase)) return RagnavikCharacterEnvironment.Test;
        throw new InvalidOperationException("Character environment must be Production or Test.");
    }

    internal static string Namespace(RagnavikCharacterEnvironment environment) => environment == RagnavikCharacterEnvironment.Test ? "test" : "prod";
}
