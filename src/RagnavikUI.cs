using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace RagnavikUI;

[BepInPlugin("lostkode.ragnavik.ui", "Ragnavik UI", "1.1.0")]
public sealed class RagnavikUIPlugin : BaseUnityPlugin
{
    internal const string PluginGuid = "lostkode.ragnavik.ui";
    private ChangelogModule? changelog;
    private void Awake()
    {
        try { changelog = new ChangelogModule(this, Config, Logger); changelog.Start(); }
        catch (Exception error) { Logger.LogError($"Changelog module disabled: {error}"); changelog?.Stop(); }
    }
    private void OnDestroy() => changelog?.Stop();
}

internal sealed class ChangelogModule
{
    private static ChangelogModule? current;
    private readonly Harmony harmony = new(RagnavikUIPlugin.PluginGuid + ".changelog");
    private readonly BaseUnityPlugin plugin;
    private readonly ManualLogSource log;
    private readonly ConfigEntry<bool> enabled;
    private readonly ConfigEntry<bool> autoOpen;
    private readonly ConfigEntry<string> endpoint;
    private readonly string cachePath;
    private GameObject? panel;
    private GameObject? menuButton;
    private TMP_Text? body;
    private string text = "Loading Ragnavik updates...";
    private bool opened;

    internal ChangelogModule(BaseUnityPlugin plugin, ConfigFile config, ManualLogSource log)
    {
        this.plugin = plugin;
        this.log = log;
        enabled = config.Bind("Ragnavik Changelog", "Enabled", true, "Show the separate Ragnavik changelog panel.");
        autoOpen = config.Bind("Ragnavik Changelog", "Auto Open", true, "Open it once per game session.");
        endpoint = config.Bind("Ragnavik Changelog", "Website Endpoint", "https://ragnavik.vercel.app/api/changelog", "Public website endpoint used for Ragnavik updates.");
        cachePath = Path.Combine(Paths.ConfigPath, "RagnavikUI", "changelog-cache.json");
    }

    internal void Start()
    {
        if (!enabled.Value) return;
        current = this;
        harmony.PatchAll(typeof(Patches));
        LoadCache();
        plugin.StartCoroutine(Fetch());
    }

    internal void Stop()
    {
        harmony.UnpatchSelf();
        if (panel != null) UnityEngine.Object.Destroy(panel);
        if (menuButton != null) UnityEngine.Object.Destroy(menuButton);
        if (ReferenceEquals(current, this)) current = null;
    }

    private IEnumerator Fetch()
    {
        using UnityWebRequest request = UnityWebRequest.Get(endpoint.Value);
        request.timeout = 10;
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            if (text.StartsWith("Loading Ragnavik", StringComparison.Ordinal))
            {
                text = "Ragnavik updates are temporarily unavailable.\n\nVisit ragnavik.vercel.app/changelog.";
                UpdateBody();
            }
            log.LogWarning($"Website changelog unavailable; using cached data: {request.error}");
            yield break;
        }
        string json = request.downloadHandler.text;
        if (!TryRender(json, out string rendered)) yield break;
        text = rendered;
        UpdateBody();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            File.WriteAllText(cachePath, json);
        }
        catch (Exception error) { log.LogWarning($"Could not cache website changelog: {error.Message}"); }
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(cachePath) && TryRender(File.ReadAllText(cachePath), out string rendered)) text = rendered;
        }
        catch (Exception error) { log.LogWarning($"Could not read cached changelog: {error.Message}"); }
    }

    private bool TryRender(string json, out string rendered)
    {
        rendered = string.Empty;
        try
        {
            ChangelogFeed? feed = JsonUtility.FromJson<ChangelogFeed>(json);
            if (feed == null || feed.schemaVersion != 1 || feed.entries == null || feed.entries.Length == 0) return false;
            StringBuilder builder = new();
            foreach (ChangelogEntry entry in feed.entries)
            {
                builder.Append("<size=26><b>").Append(entry.version).Append(": ").Append(entry.title).AppendLine("</b></size>");
                builder.AppendLine(entry.publishedAt);
                if (entry.changes != null) foreach (string change in entry.changes) builder.Append("• ").AppendLine(change);
                builder.AppendLine();
            }
            rendered = builder.ToString().Trim();
            return rendered.Length > 0;
        }
        catch (Exception error) { log.LogWarning($"Website changelog response was invalid: {error.Message}"); return false; }
    }

    private void EnsureUi(FejdStartup startup)
    {
        if (panel != null || startup.m_changeLog == null || startup.m_showChangelogButton == null) return;
        panel = UnityEngine.Object.Instantiate(startup.m_changeLog, startup.m_changeLog.transform.parent);
        panel.name = "RagnavikChangelogPanel";
        ChangeLog? vanillaComponent = panel.GetComponent<ChangeLog>();
        if (vanillaComponent != null) UnityEngine.Object.DestroyImmediate(vanillaComponent);
        TMP_Text[] labels = panel.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text label in labels)
        {
            string name = label.gameObject.name.ToLowerInvariant();
            if (name.Contains("title") || name.Contains("header")) label.text = "Ragnavik Changelog";
            else if (body == null || label.rectTransform.rect.width * label.rectTransform.rect.height > body.rectTransform.rect.width * body.rectTransform.rect.height) body = label;
        }
        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
        {
            string name = button.gameObject.name.ToLowerInvariant();
            if (!name.Contains("close") && !name.Contains("back")) continue;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => panel.SetActive(false));
        }
        panel.SetActive(false);
        menuButton = UnityEngine.Object.Instantiate(startup.m_showChangelogButton, startup.m_showChangelogButton.transform.parent);
        menuButton.name = "RagnavikChangelogButton";
        RectTransform? rect = menuButton.GetComponent<RectTransform>();
        if (rect != null) rect.anchoredPosition += new Vector2(0f, -48f);
        Button action = menuButton.GetComponent<Button>();
        action.onClick.RemoveAllListeners();
        action.onClick.AddListener(() => panel.SetActive(!panel.activeSelf));
        TMP_Text? buttonLabel = menuButton.GetComponentInChildren<TMP_Text>(true);
        if (buttonLabel != null) buttonLabel.text = "Ragnavik Updates";
        UpdateBody();
    }

    private void UpdateBody() { if (body != null) body.text = text; }

    private void Show(FejdStartup startup)
    {
        EnsureUi(startup);
        menuButton?.SetActive(true);
        if (autoOpen.Value && !opened && panel != null) { panel.SetActive(true); opened = true; }
    }

    private void Hide() { if (panel != null) panel.SetActive(false); if (menuButton != null) menuButton.SetActive(false); }

    private static class Patches
    {
        [HarmonyPatch(typeof(FejdStartup), "Start"), HarmonyPostfix]
        private static void InitialMenu(FejdStartup __instance) { try { current?.Show(__instance); } catch (Exception error) { current?.log.LogWarning(error); } }
        [HarmonyPatch(typeof(FejdStartup), "ShowStartGame"), HarmonyPostfix]
        private static void Show(FejdStartup __instance) { try { current?.Show(__instance); } catch (Exception error) { current?.log.LogWarning(error); } }
        [HarmonyPatch(typeof(FejdStartup), "ShowCharacterSelection"), HarmonyPostfix]
        private static void HideCharacters() => current?.Hide();
        [HarmonyPatch(typeof(FejdStartup), "LoadMainScene"), HarmonyPrefix]
        private static void HideLoading() => current?.Hide();
    }
}

[Serializable] internal sealed class ChangelogFeed { public int schemaVersion; public ChangelogEntry[]? entries; }
[Serializable] internal sealed class ChangelogEntry { public string version = ""; public string publishedAt = ""; public string title = ""; public string[]? changes; }
