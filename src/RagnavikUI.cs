using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
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
    private FejdStartup? menu;
    private readonly Vector3[] corners = new Vector3[4];

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
        Canvas.willRenderCanvases -= PositionMenu;
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
        if (!TryRender(json, out string rendered))
        {
            if (text.StartsWith("Loading Ragnavik", StringComparison.Ordinal))
                text = "Ragnavik updates are temporarily unavailable.";
            UpdateBody();
            yield break;
        }
        text = rendered;
        log.LogInfo("Loaded Ragnavik changelog from the website.");
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
            ChangelogFeed? feed = JsonConvert.DeserializeObject<ChangelogFeed>(json);
            if (feed == null || feed.schemaVersion != 1 || feed.entries == null || feed.entries.Length == 0)
            {
                log.LogWarning($"Changelog schema rejected: version={feed?.schemaVersion}, entries={feed?.entries?.Length}, response length={json.Length}");
                return false;
            }
            StringBuilder builder = new();
            foreach (ChangelogEntry entry in feed.entries)
            {
                builder.Append("<b>").Append(entry.version).Append(": ").Append(entry.title).AppendLine("</b>");
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
        body = vanillaComponent?.m_textField;
        TMP_Text? originalBody = startup.m_changeLog.GetComponent<ChangeLog>()?.m_textField;
        if (body != null && originalBody != null)
        {
            body.enableAutoSizing = false;
            body.fontSize = originalBody.enableAutoSizing ? originalBody.fontSizeMin : originalBody.fontSize;
        }
        if (vanillaComponent != null) UnityEngine.Object.DestroyImmediate(vanillaComponent);
        TMP_Text[] labels = panel.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text label in labels)
        {
            string name = label.gameObject.name.ToLowerInvariant();
            if (label != body && (name.Contains("title") || name.Contains("header") || label.text.Contains("$menu_changelog") || label.text == "Changelog")) label.text = "Ragnavik Changelog";
        }
        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
        {
            string name = button.gameObject.name.ToLowerInvariant();
            if (!name.Contains("close") && !name.Contains("back")) continue;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => panel.SetActive(false));
        }
        panel.SetActive(false);
        RectTransform originalRect = startup.m_showChangelogButton.GetComponent<RectTransform>();
        float linkSpacing = float.PositiveInfinity;
        foreach (Transform sibling in originalRect.parent)
        {
            if (sibling == originalRect || sibling.GetComponent<Button>() == null) continue;
            RectTransform? siblingRect = sibling as RectTransform;
            if (siblingRect == null) continue;
            float gap = originalRect.localPosition.y - siblingRect.localPosition.y;
            if (gap > 0f && gap < linkSpacing) linkSpacing = gap;
        }
        if (float.IsPositiveInfinity(linkSpacing)) linkSpacing = originalRect.rect.height;
        menuButton = UnityEngine.Object.Instantiate(startup.m_showChangelogButton, startup.m_showChangelogButton.transform.parent);
        menuButton.name = "RagnavikChangelogButton";
        LayoutElement element = menuButton.GetComponent<LayoutElement>() ?? menuButton.AddComponent<LayoutElement>();
        element.ignoreLayout = true;
        RectTransform? rect = menuButton.GetComponent<RectTransform>();
        // Layout groups position children by sibling order and overwrite manual offsets.
        menuButton.transform.SetSiblingIndex(originalRect.GetSiblingIndex());
        LayoutGroup? linkLayout = originalRect.parent.GetComponent<LayoutGroup>();
        if (linkLayout == null && rect != null)
            rect.localPosition = originalRect.localPosition + new Vector3(0f, linkSpacing, 0f);
        Button action = menuButton.GetComponent<Button>();
        action.onClick = new Button.ButtonClickedEvent();
        action.onClick.AddListener(() => panel.SetActive(!panel.activeSelf));
        TMP_Text? buttonLabel = menuButton.GetComponentInChildren<TMP_Text>(true);
        if (buttonLabel != null) buttonLabel.text = "Ragnavik Updates";
        if (linkLayout != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)originalRect.parent);
        UpdateBody();
        menu = startup;
        Canvas.willRenderCanvases -= PositionMenu;
        Canvas.willRenderCanvases += PositionMenu;
        log.LogInfo("Created Ragnavik Updates menu button and changelog panel.");
    }

    private void UpdateBody() { if (body != null) body.text = text; }

    private void Show(FejdStartup startup)
    {
        EnsureUi(startup);
        menuButton?.SetActive(true);
        if (autoOpen.Value && !opened && panel != null) { panel.SetActive(true); opened = true; }
    }

    private void Hide() { if (panel != null) panel.SetActive(false); if (menuButton != null) menuButton.SetActive(false); }

    private void RefreshMenu(FejdStartup startup)
    {
        if (startup.m_mainMenu != null && startup.m_mainMenu.activeInHierarchy) Show(startup);
        else Hide();
    }

    private void PositionMenu()
    {
        if (menuButton == null || menu == null || !menuButton.activeInHierarchy) return;
        FejdStartup startup = menu;
        RectTransform original = startup.m_showChangelogButton.GetComponent<RectTransform>();
        TMP_Text? originalLabel = original.GetComponentInChildren<TMP_Text>(true);
        TMP_Text? newLabel = menuButton.GetComponentInChildren<TMP_Text>(true);
        if (originalLabel == null || newLabel == null) return;
        Vector3 originalCenter = originalLabel.rectTransform.TransformPoint(originalLabel.rectTransform.rect.center);
        float gap = float.PositiveInfinity;
        foreach (Transform sibling in original.parent)
        {
            if (sibling == original || sibling == menuButton.transform || !sibling.gameObject.activeSelf || sibling.GetComponent<Button>() == null) continue;
            TMP_Text? label = sibling.GetComponentInChildren<TMP_Text>(true);
            if (label == null) continue;
            float distance = originalCenter.y - label.rectTransform.TransformPoint(label.rectTransform.rect.center).y;
            if (distance > 0f && distance < gap) gap = distance;
        }
        if (!float.IsPositiveInfinity(gap))
        {
            Vector3 newCenter = newLabel.rectTransform.TransformPoint(newLabel.rectTransform.rect.center);
            menuButton.transform.position += new Vector3(0f, originalCenter.y + gap - newCenter.y, 0f);
        }
        if (panel != null && panel.activeInHierarchy)
        {
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            newLabel.rectTransform.GetWorldCorners(corners);
            float buttonTop = corners[1].y;
            panelRect.GetWorldCorners(corners);
            Canvas canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
            float pixelsToWorld = canvas.transform.lossyScale.y / canvas.scaleFactor;
            panelRect.position += new Vector3(0f, buttonTop + 20f * pixelsToWorld - corners[0].y, 0f);
        }
    }

    private static class Patches
    {
        [HarmonyPatch(typeof(FejdStartup), "Start"), HarmonyPostfix]
        private static void InitialMenu(FejdStartup __instance) { try { current?.Show(__instance); } catch (Exception error) { current?.log.LogWarning(error); } }
        [HarmonyPatch(typeof(FejdStartup), "Update"), HarmonyPostfix]
        private static void UpdateMenu(FejdStartup __instance) { current?.RefreshMenu(__instance); }
        [HarmonyPatch(typeof(FejdStartup), "ShowCharacterSelection"), HarmonyPostfix]
        private static void HideCharacters() => current?.Hide();
        [HarmonyPatch(typeof(FejdStartup), "LoadMainScene"), HarmonyPrefix]
        private static void HideLoading() => current?.Hide();
    }
}

[Serializable] internal sealed class ChangelogFeed { public int schemaVersion; public ChangelogEntry[]? entries; }
[Serializable] internal sealed class ChangelogEntry { public string version = ""; public string publishedAt = ""; public string title = ""; public string[]? changes; }
