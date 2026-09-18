using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.IO;

namespace RagnavikUI;

[BepInPlugin("lostkode.ragnavik.ui", "Ragnavik UI", "1.1.0")]
public sealed class RagnavikUIPlugin : BaseUnityPlugin
{
    internal const string PluginGuid = "lostkode.ragnavik.ui";
    private ChangelogModule? changelog;
    private void Awake()
    {
        try { changelog = new ChangelogModule(Config, Logger); changelog.Start(); }
        catch (Exception error) { Logger.LogError($"Changelog module disabled: {error}"); changelog?.Stop(); }
    }
    private void OnDestroy() => changelog?.Stop();
}

internal sealed class ChangelogModule
{
    private static ChangelogModule? current;
    private readonly Harmony harmony = new(RagnavikUIPlugin.PluginGuid + ".changelog");
    private readonly ManualLogSource log;
    private readonly ConfigEntry<bool> enabled;
    private readonly ConfigEntry<bool> autoOpen;
    private readonly ConfigEntry<bool> replaceVanilla;
    private readonly string source;
    private string text = string.Empty;
    private bool opened;

    internal ChangelogModule(ConfigFile config, ManualLogSource log)
    {
        this.log = log;
        enabled = config.Bind("Changelog", "Enabled", true, "Show the Ragnavik changelog.");
        autoOpen = config.Bind("Changelog", "Auto Open", true, "Open it once per game session.");
        replaceVanilla = config.Bind("Changelog", "Override Vanilla Text", true, "Replace rather than prepend Valheim's changelog.");
        source = Path.Combine(Paths.ConfigPath, "RagnavikUI", "changelog.txt");
    }
    internal void Start()
    {
        if (!enabled.Value) return;
        try { text = File.Exists(source) ? File.ReadAllText(source).Trim() : string.Empty; }
        catch (Exception error) { log.LogWarning($"Could not read Ragnavik changelog: {error.Message}"); }
        current = this;
        harmony.PatchAll(typeof(Patches));
        log.LogInfo(text.Length > 0 ? $"Loaded changelog from {source}" : "Ragnavik changelog missing; using Valheim's changelog");
    }
    internal void Stop() { harmony.UnpatchSelf(); if (ReferenceEquals(current, this)) current = null; }

    private static class Patches
    {
        [HarmonyPatch(typeof(ChangeLog), nameof(ChangeLog.GetPlatformText)), HarmonyPostfix]
        private static void Text(ref string __result)
        {
            if (current?.text.Length > 0) __result = current.replaceVanilla.Value ? current.text : current.text + Environment.NewLine + Environment.NewLine + __result;
        }
        [HarmonyPatch(typeof(FejdStartup), "ShowStartGame"), HarmonyPostfix]
        private static void Show(FejdStartup __instance)
        {
            bool ready = current?.text.Length > 0;
            __instance.m_showChangelogButton?.SetActive(ready);
            if (ready && current!.autoOpen.Value && !current.opened && __instance.m_changeLog != null) { __instance.m_changeLog.SetActive(true); current.opened = true; }
        }
        [HarmonyPatch(typeof(FejdStartup), "ShowCharacterSelection"), HarmonyPostfix]
        private static void Hide(FejdStartup __instance) => __instance.m_changeLog?.SetActive(false);
        [HarmonyPatch(typeof(FejdStartup), "LoadMainScene"), HarmonyPrefix]
        private static void HideLoading(FejdStartup __instance) => __instance.m_changeLog?.SetActive(false);
    }
}
