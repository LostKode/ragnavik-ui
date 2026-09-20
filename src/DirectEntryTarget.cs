using System;
using System.Collections.Generic;
using System.IO;

namespace RagnavikUI;

internal sealed class DirectEntryTarget
{
    internal const string EnvironmentToken = "__RAGNAVIK_ENTRY_ENVIRONMENT__";
    internal const string AddressToken = "__RAGNAVIK_SERVER_ADDRESS__";
    internal const string PortToken = "__RAGNAVIK_SERVER_PORT__";
    internal bool IsTest { get; }
    internal RagnavikCharacterEnvironment Environment => IsTest ? RagnavikCharacterEnvironment.Test : RagnavikCharacterEnvironment.Production;
    internal string Address { get; }
    internal ushort Port { get; }
    internal string DisplayName => IsTest ? "local test game" : "Ragnavik server";
    internal string PlayLabel => IsTest ? "Play Ragnavik Test" : "Start Game";
    private DirectEntryTarget(bool isTest, string address, ushort port) { IsTest = isTest; Address = address; Port = port; }

    internal static DirectEntryTarget Load(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Direct entry environment file is missing.");
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            int separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1) throw new InvalidOperationException("Direct entry environment file is invalid.");
            if (!values.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim())) throw new InvalidOperationException("Direct entry environment file contains duplicate keys.");
        }
        if (!values.TryGetValue("Environment", out string? environment) || !values.TryGetValue("Address", out string? address) || !values.TryGetValue("Port", out string? portText)) throw new InvalidOperationException("Direct entry environment file is incomplete.");
        if (environment.Contains(EnvironmentToken, StringComparison.Ordinal) || address.Contains(AddressToken, StringComparison.Ordinal) || portText.Contains(PortToken, StringComparison.Ordinal)) throw new InvalidOperationException("Direct entry build tokens were not injected.");
        bool isTest = environment.Equals("Test", StringComparison.OrdinalIgnoreCase);
        if (!isTest && !environment.Equals("Production", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Direct entry environment must be Production or Test.");
        if (string.IsNullOrWhiteSpace(address) || address.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0) throw new InvalidOperationException("Direct entry address is invalid.");
        if (!ushort.TryParse(portText, out ushort port) || port == 0) throw new InvalidOperationException("Direct entry port is invalid.");
        if (isTest && !IsLocalAddress(address)) throw new InvalidOperationException("Test Mode build metadata requires a loopback or private-network placeholder.");
        return new DirectEntryTarget(isTest, address, port);
    }

    private static bool IsLocalAddress(string address)
    {
        string value = address.ToLowerInvariant();
        if (value == "localhost" || value == "::1" || value.StartsWith("127.") || value.StartsWith("10.") || value.StartsWith("192.168.")) return true;
        if (!value.StartsWith("172.")) return false;
        string[] parts = value.Split('.');
        return parts.Length == 4 && int.TryParse(parts[1], out int second) && second is >= 16 and <= 31;
    }
}
