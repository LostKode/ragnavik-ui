using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RagnavikUI;

internal sealed class RagnavikCharacterRegistry
{
    private const int SchemaVersion = 1;
    private readonly string path;
    private readonly List<Entry> entries;

    internal RagnavikCharacterRegistry(string path)
    {
        this.path = path;
        entries = Load(path);
    }

    internal bool Owns(PlayerProfile profile, RagnavikCharacterEnvironment environment)
    {
        string filename = profile.GetFilename();
        if (!HasExpectedPrefix(filename, environment)) return false;
        return entries.Any(entry =>
            entry.environment == environment.ToString() &&
            entry.filename == filename &&
            entry.playerId == profile.GetPlayerID());
    }

    internal bool OwnsFilename(string filename, RagnavikCharacterEnvironment environment) =>
        HasExpectedPrefix(filename, environment) && entries.Any(entry => entry.environment == environment.ToString() && entry.filename == filename);

    internal void Add(PlayerProfile profile, RagnavikCharacterEnvironment environment)
    {
        string filename = profile.GetFilename();
        if (!HasExpectedPrefix(filename, environment)) throw new InvalidOperationException("Ragnavik character filename has the wrong environment prefix.");
        if (entries.Any(entry => entry.filename == filename)) throw new InvalidOperationException("Ragnavik character filename is already registered.");
        Entry entry = new() { filename = filename, environment = environment.ToString(), playerId = profile.GetPlayerID() };
        entries.Add(entry);
        try
        {
            Save();
        }
        catch
        {
            entries.Remove(entry);
            throw;
        }
    }

    internal void Remove(string filename, RagnavikCharacterEnvironment environment)
    {
        int removed = entries.RemoveAll(entry => entry.filename == filename && entry.environment == environment.ToString());
        if (removed > 0) Save();
    }

    internal static string CreateFilename(RagnavikCharacterEnvironment environment) =>
        $"ragnavik_{RagnavikCharacterEnvironmentSource.Namespace(environment)}_{Guid.NewGuid():N}";

    private static bool HasExpectedPrefix(string filename, RagnavikCharacterEnvironment environment) =>
        filename.StartsWith($"ragnavik_{RagnavikCharacterEnvironmentSource.Namespace(environment)}_", StringComparison.Ordinal);

    private static List<Entry> Load(string path)
    {
        if (!File.Exists(path)) return new List<Entry>();
        Document? document = JsonConvert.DeserializeObject<Document>(File.ReadAllText(path));
        if (document == null || document.schemaVersion != SchemaVersion || document.entries == null) throw new InvalidOperationException("Ragnavik character registry is invalid.");
        HashSet<string> filenames = new(StringComparer.Ordinal);
        foreach (Entry entry in document.entries)
        {
            if (string.IsNullOrWhiteSpace(entry.filename) || !filenames.Add(entry.filename)) throw new InvalidOperationException("Ragnavik character registry contains an invalid or duplicate filename.");
            if (!Enum.TryParse(entry.environment, false, out RagnavikCharacterEnvironment environment) || !HasExpectedPrefix(entry.filename, environment)) throw new InvalidOperationException("Ragnavik character registry contains an invalid environment.");
            if (entry.playerId == 0) throw new InvalidOperationException("Ragnavik character registry contains an invalid character identity.");
        }
        return document.entries;
    }

    private void Save()
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Ragnavik character registry path is invalid.");
        Directory.CreateDirectory(directory);
        string temporaryPath = path + ".new";
        string backupPath = path + ".old";
        string json = JsonConvert.SerializeObject(new Document { schemaVersion = SchemaVersion, entries = entries }, Formatting.Indented);
        File.WriteAllText(temporaryPath, json);
        if (File.Exists(path)) File.Replace(temporaryPath, path, backupPath);
        else File.Move(temporaryPath, path);
    }

    private sealed class Document
    {
        public int schemaVersion;
        public List<Entry>? entries;
    }

    private sealed class Entry
    {
        public string filename = string.Empty;
        public string environment = string.Empty;
        public long playerId;
    }
}
