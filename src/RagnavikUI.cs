using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace RagnavikUI;

[BepInPlugin("lostkode.ragnavik.ui", "Ragnavik UI", "1.2.15")]
public sealed class RagnavikUIPlugin : BaseUnityPlugin
{
    internal const string PluginGuid = "lostkode.ragnavik.ui";
    private ChangelogModule? changelog;
    private DirectEntryModule? directEntry;
    private RagnavikCharacterSelectionModule? characterSelection;
    private LoadingScreenModule? loadingScreens;
    private void Awake()
    {
        try { characterSelection = new RagnavikCharacterSelectionModule(Logger); characterSelection.Start(); }
        catch (Exception error) { Logger.LogError($"Ragnavik character selector disabled: {error}"); characterSelection?.Stop(); }
        try { loadingScreens = new LoadingScreenModule(this, Logger); loadingScreens.Start(); }
        catch (Exception error) { Logger.LogError($"Loading screen module disabled: {error}"); loadingScreens?.Stop(); }
        try { changelog = new ChangelogModule(this, Config, Logger); changelog.Start(); }
        catch (Exception error) { Logger.LogError($"Changelog module disabled: {error}"); changelog?.Stop(); }
        try { directEntry = new DirectEntryModule(Logger); directEntry.Start(); }
        catch (Exception error) { Logger.LogError($"Direct entry module disabled: {error.Message}"); directEntry?.Stop(); }
    }
    private void OnDestroy()
    {
        directEntry?.Stop();
        changelog?.Stop();
        characterSelection?.Stop();
        loadingScreens?.Stop();
    }
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
    private readonly ConfigEntry<string> discordUrl;
    private readonly ConfigEntry<string> supportUrl;
    private readonly string cachePath;
    private GameObject? panel;
    private GameObject? menuButton;
    private TMP_Text? body;
    private string text = "Loading Ragnavik updates...";
    private bool opened;
    private FejdStartup? menu;
    private readonly Vector3[] corners = new Vector3[4];
    private float vanillaPanelAnchoredY;
    private ScrollRect? panelScroll;
    private bool resetScrollToTop;
    private Sprite? ragnavikLogo;
    private Image? menuLogoImage;
    private bool logoLoadAttempted;
    private bool logoNotFoundLogged;
    private Sprite? discordLogo;
    private Sprite? supportLogo;
    private GameObject? communityPanel;

    internal ChangelogModule(BaseUnityPlugin plugin, ConfigFile config, ManualLogSource log)
    {
        this.plugin = plugin;
        this.log = log;
        enabled = config.Bind("Ragnavik Changelog", "Enabled", true, "Show the separate Ragnavik changelog panel.");
        autoOpen = config.Bind("Ragnavik Changelog", "Auto Open", true, "Open it once per game session.");
        endpoint = config.Bind("Ragnavik Changelog", "Website Endpoint", "https://ragnavik.vercel.app/api/changelog", "Public website endpoint used for Ragnavik updates.");
        discordUrl = config.Bind("Community Links", "Discord URL", "https://discord.gg/TbmFWbkxZR", "Discord invite opened from the main menu.");
        supportUrl = config.Bind("Community Links", "Support URL", "https://buymeacoffee.com/ragnavik", "Support page opened from the main menu.");
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
        if (communityPanel != null) UnityEngine.Object.Destroy(communityPanel);
        if (ragnavikLogo != null)
        {
            UnityEngine.Object.Destroy(ragnavikLogo.texture);
            UnityEngine.Object.Destroy(ragnavikLogo);
        }
        if (discordLogo != null)
        {
            UnityEngine.Object.Destroy(discordLogo.texture);
            UnityEngine.Object.Destroy(discordLogo);
        }
        if (supportLogo != null)
        {
            UnityEngine.Object.Destroy(supportLogo.texture);
            UnityEngine.Object.Destroy(supportLogo);
        }
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

    private static GameObject? GetChangeLog(FejdStartup startup) => Traverse.Create(startup).Field<GameObject>("m_changeLog").Value;
    private static GameObject? GetShowChangelogButton(FejdStartup startup) => Traverse.Create(startup).Field<GameObject>("m_showChangelogButton").Value;
    private static GameObject? GetMerchStoreButtonParent(FejdStartup startup) => Traverse.Create(startup).Field<GameObject>("m_merchStoreButtonParent").Value;

    private void EnsureUi(FejdStartup startup)
    {
        if (panel != null || GetChangeLog(startup) == null || GetShowChangelogButton(startup) == null) return;
        EnsureCommunityPanel(startup);
        panel = UnityEngine.Object.Instantiate(GetChangeLog(startup), GetChangeLog(startup).transform.parent);
        vanillaPanelAnchoredY = GetChangeLog(startup).GetComponent<RectTransform>().anchoredPosition.y;
        panel.name = "RagnavikChangelogPanel";
        panelScroll = panel.GetComponentInChildren<ScrollRect>(true);
        ChangeLog? vanillaComponent = panel.GetComponent<ChangeLog>();
        body = vanillaComponent?.m_textField;
        TMP_Text? originalBody = GetChangeLog(startup).GetComponent<ChangeLog>()?.m_textField;
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
        RectTransform originalRect = GetShowChangelogButton(startup).GetComponent<RectTransform>();
        menuButton = UnityEngine.Object.Instantiate(GetShowChangelogButton(startup), GetShowChangelogButton(startup).transform.parent);
        menuButton.name = "RagnavikChangelogButton";
        // Use Valheim's native vertical group for placement, spacing, and menu fading.
        menuButton.transform.SetSiblingIndex(originalRect.GetSiblingIndex());
        LayoutElement element = menuButton.GetComponent<LayoutElement>() ?? menuButton.AddComponent<LayoutElement>();
        // Keep the original three rows at their native positions. The fixed-height group
        // already has one row of unused space above Changelog for the custom control.
        element.ignoreLayout = true;
        LayoutGroup? linkLayout = originalRect.parent.GetComponent<LayoutGroup>();
        Button action = menuButton.GetComponent<Button>();
        action.onClick = new Button.ButtonClickedEvent();
        action.onClick.AddListener(() =>
        {
            bool show = !panel.activeSelf;
            GetChangeLog(startup).SetActive(false);
            panel.SetActive(show);
            if (show) resetScrollToTop = true;
        });
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

    private void UpdateBody()
    {
        if (body != null) body.text = text;
        resetScrollToTop = true;
    }

    private void ResetPanelScroll()
    {
        if (!resetScrollToTop || panel == null || !panel.activeInHierarchy || panelScroll == null) return;
        // Text and ContentSizeFitter must have their final height before normalization.
        body?.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelScroll.GetComponent<RectTransform>());
        panelScroll.Rebuild(CanvasUpdate.PostLayout);
        panelScroll.StopMovement();
        panelScroll.verticalNormalizedPosition = 1f;
        resetScrollToTop = false;
    }

    private void Show(FejdStartup startup)
    {
        EnsureLogo(startup);
        EnsureUi(startup);
        menuButton?.SetActive(true);
        if (autoOpen.Value && !opened && panel != null)
        {
            GetChangeLog(startup).SetActive(false);
            panel.SetActive(true);
            resetScrollToTop = true;
            opened = true;
        }
    }

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
        if (menuButton != null) menuButton.SetActive(false);
    }

    private void RefreshMenu(FejdStartup startup)
    {
        if (startup.m_mainMenu != null && startup.m_mainMenu.activeInHierarchy) Show(startup);
        else Hide();
    }

    private void PositionMenu()
    {
        if (menuButton == null || menu == null || !menuButton.activeInHierarchy) return;
        ResetPanelScroll();
        FejdStartup startup = menu;
        RectTransform originalButton = GetShowChangelogButton(startup).GetComponent<RectTransform>();
        RectTransform customButton = menuButton.GetComponent<RectTransform>();
        TMP_Text? originalLabel = originalButton.GetComponentInChildren<TMP_Text>(true);
        TMP_Text? customLabel = customButton.GetComponentInChildren<TMP_Text>(true);
        if (originalLabel != null && customLabel != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(customButton);
            RectTransform group = (RectTransform)originalButton.parent;
            float originalCenter = group.InverseTransformPoint(originalLabel.rectTransform.TransformPoint(originalLabel.textBounds.center)).y;
            float nativeGap = float.PositiveInfinity;
            foreach (Transform sibling in group)
            {
                if (sibling == originalButton || sibling == customButton || !sibling.gameObject.activeSelf || sibling.GetComponent<Button>() == null) continue;
                TMP_Text? siblingLabel = sibling.GetComponentInChildren<TMP_Text>(true);
                if (siblingLabel == null) continue;
                siblingLabel.ForceMeshUpdate();
                float siblingCenter = group.InverseTransformPoint(siblingLabel.rectTransform.TransformPoint(siblingLabel.textBounds.center)).y;
                float distance = originalCenter - siblingCenter;
                if (distance > 0f && distance < nativeGap) nativeGap = distance;
            }
            if (!float.IsPositiveInfinity(nativeGap))
            {
                customLabel.ForceMeshUpdate();
                float customCenter = group.InverseTransformPoint(customLabel.rectTransform.TransformPoint(customLabel.textBounds.center)).y;
                Vector3 position = customButton.localPosition;
                position.x = originalButton.localPosition.x;
                position.y += originalCenter + nativeGap - customCenter;
                customButton.localPosition = position;
            }
        }
        Canvas canvas = menuButton.GetComponentInParent<Canvas>().rootCanvas;
        RectTransform canvasRect = (RectTransform)canvas.transform;
        if (communityPanel != null)
        {
            RectTransform communityRect = communityPanel.GetComponent<RectTransform>();
            RectTransform communityParent = (RectTransform)communityRect.parent;
            Vector2 communityPosition = communityRect.anchoredPosition;
            communityPosition.y = 28f + communityParent.rect.height * 0.03f;
            communityRect.anchoredPosition = communityPosition;
        }
        if (panel != null)
        {
            RectTransform vanillaRect = GetChangeLog(startup).GetComponent<RectTransform>();
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            // Anchored offsets remain stable when the canvas resizes; local X does not.
            // Let Valheim retain its live horizontal placement and change only anchored Y.
            Vector3 shift = vanillaRect.parent.InverseTransformVector(
                canvasRect.TransformVector(new Vector3(0f, 50f / canvas.scaleFactor, 0f)));
            Vector2 nativeOffset = vanillaRect.anchoredPosition;
            nativeOffset.y = vanillaPanelAnchoredY + shift.y;
            vanillaRect.anchoredPosition = nativeOffset;
            panelRect.anchorMin = vanillaRect.anchorMin;
            panelRect.anchorMax = vanillaRect.anchorMax;
            panelRect.pivot = vanillaRect.pivot;
            panelRect.sizeDelta = vanillaRect.sizeDelta;
            panelRect.localScale = vanillaRect.localScale;
            panelRect.localRotation = vanillaRect.localRotation;
            panelRect.anchoredPosition3D = vanillaRect.anchoredPosition3D;
        }
    }

    private void EnsureLogo(FejdStartup startup)
    {
        if (ragnavikLogo == null && !logoLoadAttempted)
        {
            logoLoadAttempted = true;
            string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            string logoPath = Path.Combine(pluginDirectory, "ragnavik-fjord-gate.png");
            if (!File.Exists(logoPath))
            {
                log.LogWarning($"Ragnavik menu logo was not found at {logoPath}.");
                return;
            }
            try
            {
                Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(logoPath), false))
                {
                    UnityEngine.Object.Destroy(texture);
                    log.LogWarning("Ragnavik menu logo could not be decoded.");
                    return;
                }
                texture.name = "RagnavikFjordGateLogo";
                ragnavikLogo = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception error)
            {
                log.LogWarning($"Could not load the Ragnavik menu logo: {error.Message}");
                return;
            }
        }
        if (ragnavikLogo == null) return;

        try
        {
            foreach (Image image in startup.GetComponentsInChildren<Image>(true))
            {
                if (!image.gameObject.name.Equals("Logo DeepNorth1 .0", StringComparison.OrdinalIgnoreCase)) continue;
                bool newLogoObject = menuLogoImage != image;
                bool restoredSprite = image.sprite != ragnavikLogo;
                if (newLogoObject)
                {
                    menuLogoImage = image;
                    image.preserveAspect = true;
                    image.rectTransform.localScale *= 0.52f;
                    Vector2 logoPosition = image.rectTransform.anchoredPosition;
                    logoPosition.y -= image.rectTransform.rect.height * 0.15f;
                    image.rectTransform.anchoredPosition = logoPosition;
                }
                if (restoredSprite) image.sprite = ragnavikLogo;
                if (newLogoObject || restoredSprite)
                    log.LogInfo(newLogoObject
                        ? "Applied the Ragnavik Fjord Gate menu logo."
                        : "Restored the Ragnavik Fjord Gate menu logo after a menu transition.");
                logoNotFoundLogged = false;
                return;
            }
            if (!logoNotFoundLogged)
            {
                log.LogWarning("Valheim menu logo object was not found.");
                logoNotFoundLogged = true;
            }
        }
        catch (Exception error) { log.LogWarning($"Could not apply the Ragnavik menu logo: {error.Message}"); }
    }

    private void EnsureCommunityPanel(FejdStartup startup)
    {
        if (communityPanel != null) return;
        startup.m_moddedText?.SetActive(false);
        GetMerchStoreButtonParent(startup)?.SetActive(false);

        Transform parent = startup.m_moddedText != null ? startup.m_moddedText.transform.parent : startup.m_mainMenu.transform;
        string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
        discordLogo = LoadMenuSprite(Path.Combine(pluginDirectory, "discord.png"), "DiscordLogo");
        supportLogo = LoadMenuSprite(Path.Combine(pluginDirectory, "buymeacoffee.png"), "BuyMeACoffeeLogo");
        communityPanel = new GameObject("RagnavikCommunityPanel", typeof(RectTransform));
        RectTransform panelRect = communityPanel.GetComponent<RectTransform>();
        panelRect.SetParent(parent, false);
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.anchoredPosition = new Vector2(-28f, 28f);
        panelRect.sizeDelta = new Vector2(360f, 250f);

        CreateCommunityAction(startup, panelRect, "Discord", new Vector2(0f, 132f), new Vector2(360f, 118f),
            "NEED HELP?", "JOIN DISCORD", discordUrl.Value, discordLogo);
        CreateCommunityAction(startup, panelRect, "Support", Vector2.zero, new Vector2(360f, 122f),
            "ENJOYING THE SERVER?", "WANT TO HELP KEEP IT RUNNING?\nBUY ME A COFFEE", supportUrl.Value, supportLogo);
        log.LogInfo("Replaced the mod warning and Valheim merch link with Ragnavik community links.");
    }

    private void CreateCommunityAction(FejdStartup startup, RectTransform parent, string name, Vector2 position,
        Vector2 size, string heading, string actionText, string url, Sprite? icon)
    {
        GameObject actionObject = new("Ragnavik" + name + "Action", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform actionRect = actionObject.GetComponent<RectTransform>();
        actionRect.SetParent(parent, false);
        actionRect.anchorMin = actionRect.anchorMax = Vector2.zero;
        actionRect.pivot = Vector2.zero;
        actionRect.anchoredPosition = position;
        actionRect.sizeDelta = size;
        Image background = actionObject.GetComponent<Image>();
        background.color = new Color(0.025f, 0.025f, 0.02f, 0.48f);
        Button button = actionObject.GetComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.72f, 0.34f, 1f);
        colors.pressedColor = new Color(0.82f, 0.46f, 0.16f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => Application.OpenURL(url));

        CreateCommunityText(startup, actionRect, heading, new Vector2(0f, size.y - 34f), new Vector2(size.x, 28f), 18f);
        CreateCommunityText(startup, actionRect, actionText, new Vector2(0f, 5f), new Vector2(size.x, 38f), name == "Discord" ? 16f : 14f);

        if (icon != null)
        {
            GameObject logoObject = new(name + "Logo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform logoRect = logoObject.GetComponent<RectTransform>();
            logoRect.SetParent(actionRect, false);
            logoRect.anchorMin = logoRect.anchorMax = new Vector2(0.5f, 0f);
            logoRect.pivot = new Vector2(0.5f, 0f);
            float iconSize = name == "Support" ? 40f : 44f;
            logoRect.anchoredPosition = new Vector2(0f, name == "Support" ? 44f : 40f);
            logoRect.sizeDelta = new Vector2(iconSize, iconSize);
            Image logoImage = logoObject.GetComponent<Image>();
            logoImage.sprite = icon;
            logoImage.preserveAspect = true;
            logoImage.raycastTarget = false;
        }
    }

    private Sprite? LoadMenuSprite(string path, string name)
    {
        if (!File.Exists(path))
        {
            log.LogWarning($"Menu icon was not found at {path}.");
            return null;
        }
        try
        {
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            texture.name = name;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }
        catch (Exception error)
        {
            log.LogWarning($"Could not load {name}: {error.Message}");
            return null;
        }
    }

    private static TMP_Text CreateCommunityText(FejdStartup startup, RectTransform parent, string value,
        Vector2 position, Vector2 size, float fontSize)
    {
        GameObject textObject = UnityEngine.Object.Instantiate(startup.m_moddedText, parent);
        textObject.name = "Text";
        textObject.SetActive(true);
        TMP_Text label = textObject.GetComponent<TMP_Text>();
        foreach (MonoBehaviour component in textObject.GetComponents<MonoBehaviour>())
            if (component != label) UnityEngine.Object.DestroyImmediate(component);
        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        label.text = value;
        label.fontSize = fontSize;
        label.enableAutoSizing = false;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.65f, 0.08f, 1f);
        label.raycastTarget = false;
        return label;
    }

    private static class Patches
    {
        [HarmonyPatch(typeof(FejdStartup), "OnButtonShowChangelog"), HarmonyPrefix]
        private static void CloseRagnavikBeforeVanilla()
        {
            if (current?.panel != null) current.panel.SetActive(false);
        }
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
