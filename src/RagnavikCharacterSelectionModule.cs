using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RagnavikUI;

internal sealed class RagnavikCharacterSelectionModule
{
    private static RagnavikCharacterSelectionModule? current;
    private readonly Harmony harmony = new(RagnavikUIPlugin.PluginGuid + ".characters");
    private readonly ManualLogSource log;
    private readonly RagnavikCharacterRegistry? registry;
    private readonly RagnavikCharacterEnvironment? environment;
    private readonly string? initializationError;
    private bool selectorActive;

    internal static PlayerProfile? SelectedProfile { get; private set; }
    internal static RagnavikCharacterEnvironment? SelectedEnvironment { get; private set; }
    internal static RagnavikCharacterEnvironment? ActiveEnvironment { get; private set; }

    internal RagnavikCharacterSelectionModule(ManualLogSource log)
    {
        this.log = log;
        try
        {
            string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            environment = RagnavikCharacterEnvironmentSource.Load(Path.Combine(pluginDirectory, "character-environment.env"));
            registry = new RagnavikCharacterRegistry(Path.Combine(Paths.ConfigPath, "RagnavikUI", "characters.json"));
        }
        catch (Exception error)
        {
            initializationError = error.Message;
        }
    }

    internal void Start()
    {
        current = this;
        harmony.PatchAll(typeof(Patches));
        ActiveEnvironment = Ready ? environment : null;
        if (Ready) log.LogInfo($"Ragnavik character selector enabled for {environment}. Normal Valheim profiles remain outside this selector.");
        else log.LogError($"Ragnavik character selector is locked to protect normal Valheim profiles: {initializationError}");
    }

    internal void Stop()
    {
        harmony.UnpatchSelf();
        selectorActive = false;
        SelectedProfile = null;
        SelectedEnvironment = null;
        ActiveEnvironment = null;
        if (ReferenceEquals(current, this)) current = null;
    }

    private void Show(FejdStartup startup)
    {
        selectorActive = true;
        SelectedProfile = null;
        SelectedEnvironment = null;
        ApplyFilter(startup, null);
        Traverse.Create(startup).Method("UpdateCharacterList").GetValue();
    }

    private void ApplyFilter(FejdStartup startup, string? selectFilename)
    {
        if (!Ready) { SetProfiles(startup, new List<PlayerProfile>()); SetProfileIndex(startup, -1); return; }
        List<PlayerProfile> profiles = SaveSystem.GetAllPlayerProfiles()
            .Where(profile => registry!.Owns(profile, environment!.Value))
            .ToList();
        SetProfiles(startup, profiles);
        SetProfileIndex(startup, profiles.Count == 0 ? -1 : 0);
        if (selectFilename != null)
        {
            int index = profiles.FindIndex(profile => profile.GetFilename() == selectFilename);
            if (index >= 0) SetProfileIndex(startup, index);
        }
    }

    private bool BeginCreation(FejdStartup startup, out CreationState state)
    {
        state = new CreationState();
        if (!selectorActive) return true;
        state.active = true;
        string displayName = startup.m_csNewCharacterName.text;
        state.displayName = displayName;
        if (!Ready) { startup.m_newCharacterError.SetActive(true); state.skipped = true; return false; }
        if (GetOwnedProfiles().Any(profile => profile.GetName().Equals(displayName, StringComparison.OrdinalIgnoreCase)))
        {
            startup.m_newCharacterError.SetActive(true);
            state.skipped = true;
            return false;
        }
        state.filename = RagnavikCharacterRegistry.CreateFilename(environment!.Value);
        startup.m_csNewCharacterName.text = state.filename;
        return true;
    }

    private void FinishCreation(FejdStartup startup, CreationState? state, Exception? originalError)
    {
        if (state == null || !state.active) return;
        startup.m_csNewCharacterName.text = state.displayName;
        if (state.skipped || originalError != null) return;
        PlayerProfile? profile = SaveSystem.GetAllPlayerProfiles().FirstOrDefault(candidate => candidate.GetFilename() == state.filename);
        if (profile == null) return;
        try
        {
            profile.SetName(state.displayName);
            if (!profile.Save()) throw new InvalidOperationException("Valheim did not save the new Ragnavik character.");
            registry!.Add(profile, environment!.Value);
        }
        catch
        {
            PlayerProfile.RemoveProfile(state.filename);
            throw;
        }
        ApplyFilter(startup, state.filename);
        Traverse.Create(startup).Method("UpdateCharacterList").GetValue();
        log.LogInfo($"Created a new {environment!.Value} Ragnavik character in Valheim's normal protected save system.");
    }

    private List<PlayerProfile> GetOwnedProfiles() => Ready ? SaveSystem.GetAllPlayerProfiles().Where(profile => registry!.Owns(profile, environment!.Value)).ToList() : new List<PlayerProfile>();

    private bool BeginRemoval(FejdStartup startup, out string filename)
    {
        filename = Traverse.Create(startup).Field("m_tempRemoveCharacterName").GetValue<string>() ?? string.Empty;
        return selectorActive && Ready && registry!.OwnsFilename(filename, environment!.Value);
    }

    private void FinishRemoval(string filename, bool owned)
    {
        if (!owned) return;
        registry!.Remove(filename, environment!.Value);
        log.LogInfo($"Removed a registered {environment!.Value} Ragnavik character after Valheim deleted its profile.");
    }

    private bool SelectForHandoff(FejdStartup startup)
    {
        if (!selectorActive) return true;
        List<PlayerProfile> profiles = GetProfiles(startup);
        int profileIndex = GetProfileIndex(startup);
        if (profileIndex < 0 || profileIndex >= profiles.Count)
        {
            SelectedProfile = null;
            SelectedEnvironment = null;
            log.LogWarning($"Blocked character start because no {environment} Ragnavik character is selected.");
            return false;
        }
        PlayerProfile profile = profiles[profileIndex];
        if (!Ready || !registry!.Owns(profile, environment!.Value)) throw new InvalidOperationException("Selected character is not owned by the active Ragnavik environment.");
        SelectedProfile = profile;
        SelectedEnvironment = environment.Value;
        selectorActive = false;
        log.LogInfo($"Selected a validated {environment.Value} Ragnavik character for handoff.");
        return true;
    }

    private static List<PlayerProfile> GetProfiles(FejdStartup startup) => Traverse.Create(startup).Field("m_profiles").GetValue<List<PlayerProfile>>() ?? new List<PlayerProfile>();
    private static int GetProfileIndex(FejdStartup startup) => Traverse.Create(startup).Field("m_profileIndex").GetValue<int>();

    private bool Ready => registry != null && environment.HasValue;
    private static void SetProfiles(FejdStartup startup, List<PlayerProfile> profiles) => Traverse.Create(startup).Field("m_profiles").SetValue(profiles);
    private static void SetProfileIndex(FejdStartup startup, int index) => Traverse.Create(startup).Field("m_profileIndex").SetValue(index);

    private sealed class CreationState
    {
        internal bool active;
        internal bool skipped;
        internal string displayName = string.Empty;
        internal string filename = string.Empty;
    }

    private static class Patches
    {
        [HarmonyPatch(typeof(FejdStartup), "ShowCharacterSelection"), HarmonyPrefix]
        private static void FilterCharacterSelection(FejdStartup __instance) => current?.Show(__instance);

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnNewCharacterDone)), HarmonyPrefix]
        private static bool PrepareNewCharacter(FejdStartup __instance, out CreationState __state) => current?.BeginCreation(__instance, out __state) ?? DefaultState(out __state);

        private static bool DefaultState(out CreationState state) { state = new CreationState(); return true; }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnNewCharacterDone)), HarmonyFinalizer]
        private static Exception? CompleteNewCharacter(FejdStartup __instance, CreationState? __state, Exception? __exception)
        {
            current?.FinishCreation(__instance, __state, __exception);
            return __exception;
        }

        [HarmonyPatch(typeof(FejdStartup), "OnButtonRemoveCharacterYes"), HarmonyPrefix]
        private static void CaptureRemoval(FejdStartup __instance, out string __state)
        {
            string filename = string.Empty;
            bool owned = current?.BeginRemoval(__instance, out filename) == true;
            __state = owned ? filename : string.Empty;
        }

        [HarmonyPatch(typeof(FejdStartup), "OnButtonRemoveCharacterYes"), HarmonyPostfix]
        private static void CompleteRemoval(string __state) => current?.FinishRemoval(__state, __state.Length > 0);

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnCharacterStart)), HarmonyPrefix]
        private static bool CaptureSelection(FejdStartup __instance) => current?.SelectForHandoff(__instance) ?? true;

        [HarmonyPatch(typeof(FejdStartup), "OnSelelectCharacterBack"), HarmonyPostfix]
        private static void LeaveSelector()
        {
            if (current != null) current.selectorActive = false;
        }
    }
}
