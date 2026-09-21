using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RagnavikUI;

internal sealed class DirectEntryModule
{
    private const string LocalTestWorldName = "galetest1";
    private const string ConnectionLayoutName = "RagnavikConnectionLayout";
    private const string ConnectionTextViewportName = "TextViewport";
    private const float ConnectionDialogScreenLimit = 0.9f;
    private static DirectEntryModule? current;
    private readonly Harmony harmony = new(RagnavikUIPlugin.PluginGuid + ".direct-entry");
    private readonly ManualLogSource log;
    private readonly DirectEntryTarget target;
    private string? catosRejection;
    private FejdStartup? startup;
    private ZNet.ConnectionStatus lastFailureStatus;

    internal DirectEntryModule(ManualLogSource log)
    {
        this.log = log;
        string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
        target = DirectEntryTarget.Load(Path.Combine(pluginDirectory, "direct-entry.env"));
        if (RagnavikCharacterSelectionModule.ActiveEnvironment != target.Environment)
            throw new InvalidOperationException("Direct-entry and character environments do not match.");
    }

    internal void Start()
    {
        current = this;
        harmony.PatchAll(typeof(Patches));
        log.LogInfo($"Direct entry enabled for {target.DisplayName}. Target details are protected.");
    }

    internal void Stop()
    {
        harmony.UnpatchSelf();
        if (ReferenceEquals(current, this)) current = null;
    }

    private void Prepare(FejdStartup startup)
    {
        catosRejection = null;
        if (target.IsTest)
        {
            Traverse.Create(startup).Field("m_queuedJoinServer").SetValue(null);
            log.LogInfo("Prepared native local-world entry for Test Mode.");
            return;
        }

        ServerJoinData joinData = new(new ServerJoinDataDedicated(target.Address, target.Port));
        Traverse.Create(startup).Field("m_queuedJoinServer").SetValue(joinData);
        log.LogInfo($"Prepared direct entry for {target.DisplayName} after character selection.");
    }

    private void StartLocalTestWorld(FejdStartup startup)
    {
        if (!target.IsTest || !startup.m_startGamePanel.activeSelf) return;

        World? localWorld = FindLocalTestWorld();
        if (localWorld == null)
        {
            startup.m_newWorldName.text = LocalTestWorldName;
            startup.m_newWorldSeed.text = World.GenerateSeed();
            startup.OnNewWorldDone(forceLocal: true);
            localWorld = FindLocalTestWorld();
        }

        if (localWorld == null)
        {
            log.LogError($"Could not find or create the local {LocalTestWorldName} world. Leaving world selection open.");
            return;
        }

        Traverse.Create(startup).Field("m_world").SetValue(localWorld);
        startup.m_openServerToggle.SetIsOnWithoutNotify(false);
        startup.m_publicServerToggle.SetIsOnWithoutNotify(false);
        startup.m_crossplayServerToggle.SetIsOnWithoutNotify(false);
        startup.m_startGamePanel.SetActive(false);
        log.LogInfo($"Starting local Test Mode world {LocalTestWorldName}.");
        startup.OnWorldStart();
    }

    private static string GetWorldName(World world) => Traverse.Create(world).Field<string>("m_worldName").Value ?? string.Empty;

    private static World? FindLocalTestWorld()
    {
        foreach (World world in SaveSystem.GetWorldList())
        {
            if (GetWorldName(world).Equals(LocalTestWorldName, StringComparison.OrdinalIgnoreCase) ||
                world.m_name.Equals(LocalTestWorldName, StringComparison.OrdinalIgnoreCase))
                return world;
        }
        return null;
    }

    private void UpdateMenu(FejdStartup startup)
    {
        this.startup = startup;
        foreach (Button button in startup.m_menuList.GetComponentsInChildren<Button>(true))
        {
            if (!Invokes(button, "OnStartGame")) continue;
            TMP_Text? label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = target.PlayLabel;
            break;
        }
        EnsureTestModeLabel(startup);
    }

    private static bool Invokes(Button button, string methodName)
    {
        for (int index = 0; index < button.onClick.GetPersistentEventCount(); index++)
            if (button.onClick.GetPersistentMethodName(index) == methodName) return true;
        return false;
    }

    private void EnsureTestModeLabel(FejdStartup startup)
    {
        Transform existing = startup.m_mainMenu.transform.Find("RagnavikTestMode");
        if (!target.IsTest)
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }
        TMP_Text? label = existing?.GetComponent<TMP_Text>();
        if (label == null)
        {
            label = UnityEngine.Object.Instantiate(startup.m_versionLabel, startup.m_mainMenu.transform);
            label.name = "RagnavikTestMode";
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(500f, 40f);
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 24f;
            label.color = new Color(1f, 0.65f, 0.08f, 1f);
        }
        label.text = "TEST MODE";
        label.gameObject.SetActive(true);
    }

    private void CaptureCatosRejection(string message)
    {
        if (target.IsTest || string.IsNullOrWhiteSpace(message)) return;
        if (!message.TrimStart().StartsWith(ConnectionFailureMessages.CatosRejectionPrefix, StringComparison.OrdinalIgnoreCase)) return;
        catosRejection = message.Trim();
        log.LogWarning("The server reported a CatosAntiCheat or mod-list rejection during direct entry.");
        if (startup != null && startup.m_connectionFailedPanel.activeSelf)
            RenderFailure(startup, lastFailureStatus);
    }

    private void RecoverFromFailure(FejdStartup startup, ZNet.ConnectionStatus status)
    {
        if (target.IsTest) return;
        if (!startup.m_connectionFailedPanel.activeSelf) return;
        lastFailureStatus = status;
        startup.m_serverListPanel.SetActive(false);
        startup.m_startGamePanel.SetActive(false);
        startup.m_characterSelectScreen.SetActive(false);
        startup.m_mainMenu.SetActive(true);
        RenderFailure(startup, status);
        log.LogWarning($"Direct entry to {target.DisplayName} failed with {status}. Target details were not logged.");
    }

    private void RenderFailure(FejdStartup startup, ZNet.ConnectionStatus status)
    {
        string failureMessage = ConnectionFailureMessages.Format((int)status, catosRejection);
        startup.m_connectionFailedError.text = $"Could not connect to {target.DisplayName}.\n\n{failureMessage}\n\nPlease retry by clicking {target.PlayLabel}.";
        EnsureConnectionFailureLayout(startup);
    }

    private static void EnsureConnectionFailureLayout(FejdStartup startup)
    {
        Transform panel = startup.m_connectionFailedPanel.transform;
        RectTransform? existingLayout = panel.GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(rect => rect.name == ConnectionLayoutName);
        if (existingLayout != null)
        {
            ResizeConnectionFailureDialog(startup, existingLayout);
            LayoutRebuilder.ForceRebuildLayoutImmediate(existingLayout);
            return;
        }

        Button[] buttons = startup.m_connectionFailedPanel.GetComponentsInChildren<Button>(true);
        RectTransform textRect = startup.m_connectionFailedError.rectTransform;
        RectTransform layoutHost = FindConnectionDialogBounds(panel, textRect, buttons);

        GameObject layoutObject = new(ConnectionLayoutName, typeof(RectTransform), typeof(VerticalLayoutGroup));
        RectTransform layoutRect = (RectTransform)layoutObject.transform;
        layoutRect.SetParent(layoutHost, false);
        layoutRect.anchorMin = Vector2.zero;
        layoutRect.anchorMax = Vector2.one;
        layoutRect.offsetMin = new Vector2(18f, 18f);
        layoutRect.offsetMax = new Vector2(-18f, -18f);
        layoutRect.SetAsLastSibling();

        VerticalLayoutGroup vertical = layoutObject.GetComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(12, 12, 12, 12);
        vertical.spacing = 12f;
        vertical.childAlignment = TextAnchor.UpperCenter;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        GameObject textGroupObject = new(ConnectionTextViewportName, typeof(RectTransform), typeof(LayoutElement), typeof(RectMask2D), typeof(ScrollRect));
        RectTransform textGroup = (RectTransform)textGroupObject.transform;
        textGroup.SetParent(layoutRect, false);
        LayoutElement textLayout = textGroupObject.GetComponent<LayoutElement>();
        textLayout.minHeight = 80f;
        textLayout.flexibleHeight = 1f;

        textRect.SetParent(textGroup, false);
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        ContentSizeFitter textFitter = startup.m_connectionFailedError.GetComponent<ContentSizeFitter>() ?? startup.m_connectionFailedError.gameObject.AddComponent<ContentSizeFitter>();
        textFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        startup.m_connectionFailedError.enableWordWrapping = true;
        startup.m_connectionFailedError.overflowMode = TextOverflowModes.Overflow;
        startup.m_connectionFailedError.alignment = TextAlignmentOptions.Top;

        ScrollRect scroll = textGroupObject.GetComponent<ScrollRect>();
        scroll.viewport = textGroup;
        scroll.content = textRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        GameObject scrollbarObject = new("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        RectTransform scrollbarRect = (RectTransform)scrollbarObject.transform;
        scrollbarRect.SetParent(textGroup, false);
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(12f, 0f);
        scrollbarRect.anchoredPosition = Vector2.zero;
        Image scrollbarBackground = scrollbarObject.GetComponent<Image>();
        scrollbarBackground.color = new Color(0f, 0f, 0f, 0.35f);

        GameObject handleObject = new("Handle", typeof(RectTransform), typeof(Image));
        RectTransform handleRect = (RectTransform)handleObject.transform;
        handleRect.SetParent(scrollbarRect, false);
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = new Vector2(2f, 2f);
        handleRect.offsetMax = new Vector2(-2f, -2f);
        Image handleImage = handleObject.GetComponent<Image>();
        handleImage.color = new Color(0.82f, 0.72f, 0.52f, 0.9f);

        Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalScrollbarSpacing = -12f;

        GameObject buttonGroupObject = new("Actions", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        RectTransform buttonGroup = (RectTransform)buttonGroupObject.transform;
        buttonGroup.SetParent(layoutRect, false);
        LayoutElement buttonLayout = buttonGroupObject.GetComponent<LayoutElement>();
        buttonLayout.minHeight = 44f;
        buttonLayout.preferredHeight = 52f;
        buttonLayout.flexibleHeight = 0f;

        HorizontalLayoutGroup horizontal = buttonGroupObject.GetComponent<HorizontalLayoutGroup>();
        horizontal.spacing = 12f;
        horizontal.childAlignment = TextAnchor.MiddleCenter;
        horizontal.childControlWidth = false;
        horizontal.childControlHeight = false;
        horizontal.childForceExpandWidth = false;
        horizontal.childForceExpandHeight = false;

        foreach (Button button in buttons)
            button.transform.SetParent(buttonGroup, false);

        ResizeConnectionFailureDialog(startup, layoutRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRect);
    }

    private static void ResizeConnectionFailureDialog(FejdStartup startup, RectTransform layout)
    {
        RectTransform host = (RectTransform)layout.parent;
        RectTransform panel = (RectTransform)startup.m_connectionFailedPanel.transform;
        float maxWidth = panel.rect.width * ConnectionDialogScreenLimit;
        float maxHeight = panel.rect.height * ConnectionDialogScreenLimit;
        float minimumWidth = Mathf.Min(520f, maxWidth);
        float minimumHeight = Mathf.Min(240f, maxHeight);
        const float horizontalChrome = 84f;
        const float verticalChrome = 130f;

        TMP_Text message = startup.m_connectionFailedError;
        float naturalWidth = message.GetPreferredValues(message.text, Mathf.Infinity, Mathf.Infinity).x + horizontalChrome;
        float dialogWidth = Mathf.Clamp(naturalWidth, minimumWidth, maxWidth);
        float availableTextWidth = Mathf.Max(100f, dialogWidth - horizontalChrome);
        float textHeight = message.GetPreferredValues(message.text, availableTextWidth, Mathf.Infinity).y;
        float dialogHeight = Mathf.Clamp(textHeight + verticalChrome, minimumHeight, maxHeight);

        host.anchorMin = host.anchorMax = new Vector2(0.5f, 0.5f);
        host.pivot = new Vector2(0.5f, 0.5f);
        host.anchoredPosition = Vector2.zero;
        host.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, dialogWidth);
        host.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, dialogHeight);

        RectTransform? viewport = layout.Find(ConnectionTextViewportName) as RectTransform;
        ScrollRect? scroll = viewport?.GetComponent<ScrollRect>();
        if (scroll != null)
        {
            scroll.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
        }
    }

    private static RectTransform FindConnectionDialogBounds(Transform panel, RectTransform text, Button[] buttons)
    {
        RectTransform? buttonRect = buttons.Select(button => button.transform as RectTransform).FirstOrDefault(rect => rect != null);
        if (buttonRect != null)
        {
            Vector3 buttonCenter = buttonRect.TransformPoint(buttonRect.rect.center);
            RectTransform? background = panel.GetComponentsInChildren<Image>(true)
                .Where(image => image.GetComponentInParent<Button>() == null)
                .Select(image => image.rectTransform)
                .Where(rect => rect != panel && ContainsWorldPoint(rect, buttonCenter))
                .Where(rect => rect.rect.width >= buttonRect.rect.width * 1.5f && rect.rect.height >= buttonRect.rect.height * 2f)
                .OrderBy(rect => rect.rect.width * rect.rect.height)
                .FirstOrDefault();
            if (background != null) return background;
        }

        return text.parent as RectTransform ?? (RectTransform)panel;
    }

    private static bool ContainsWorldPoint(RectTransform rect, Vector3 worldPoint)
    {
        Vector3 localPoint = rect.InverseTransformPoint(worldPoint);
        return rect.rect.Contains(new Vector2(localPoint.x, localPoint.y));
    }

    private static class Patches
    {
        [HarmonyPatch(typeof(FejdStartup), "Start"), HarmonyPostfix]
        private static void InitializeMenu(FejdStartup __instance) => current?.UpdateMenu(__instance);

        [HarmonyPatch(typeof(FejdStartup), "OnStartGame"), HarmonyPrefix]
        private static void PrepareDirectEntry(FejdStartup __instance) => current?.Prepare(__instance);
        [HarmonyPatch(typeof(FejdStartup), "OnCharacterStart"), HarmonyPostfix]
        private static void StartTestWorld(FejdStartup __instance) => current?.StartLocalTestWorld(__instance);

        [HarmonyPatch(typeof(FejdStartup), "ShowConnectError"), HarmonyPostfix]
        private static void RecoverConnectionFailure(FejdStartup __instance, ZNet.ConnectionStatus statusOverride) => current?.RecoverFromFailure(__instance, statusOverride);

        [HarmonyPatch(typeof(Chat), "RPC_ChatMessage"), HarmonyPrefix]
        private static void CaptureServerRejection(string text) => current?.CaptureCatosRejection(text);
    }
}
