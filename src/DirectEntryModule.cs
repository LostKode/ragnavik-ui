using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RagnavikUI;

internal sealed class DirectEntryModule
{
    private const string LocalTestWorldName = "galetest1";
    private const string ConnectionLayoutName = "RagnavikConnectionLayout";
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
        RectTransform? existingLayout = panel.Find(ConnectionLayoutName) as RectTransform;
        if (existingLayout != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(existingLayout);
            return;
        }

        Button[] buttons = startup.m_connectionFailedPanel.GetComponentsInChildren<Button>(true);

        GameObject layoutObject = new(ConnectionLayoutName, typeof(RectTransform), typeof(VerticalLayoutGroup));
        RectTransform layoutRect = (RectTransform)layoutObject.transform;
        layoutRect.SetParent(panel, false);
        layoutRect.anchorMin = new Vector2(0.08f, 0.08f);
        layoutRect.anchorMax = new Vector2(0.92f, 0.92f);
        layoutRect.offsetMin = Vector2.zero;
        layoutRect.offsetMax = Vector2.zero;
        layoutRect.SetAsLastSibling();

        VerticalLayoutGroup vertical = layoutObject.GetComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(18, 18, 18, 18);
        vertical.spacing = 18f;
        vertical.childAlignment = TextAnchor.UpperCenter;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        GameObject textGroupObject = new("Text", typeof(RectTransform), typeof(LayoutElement));
        RectTransform textGroup = (RectTransform)textGroupObject.transform;
        textGroup.SetParent(layoutRect, false);
        LayoutElement textLayout = textGroupObject.GetComponent<LayoutElement>();
        textLayout.minHeight = 120f;
        textLayout.flexibleHeight = 1f;

        RectTransform textRect = startup.m_connectionFailedError.rectTransform;
        textRect.SetParent(textGroup, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        startup.m_connectionFailedError.enableWordWrapping = true;
        startup.m_connectionFailedError.overflowMode = TextOverflowModes.Ellipsis;
        startup.m_connectionFailedError.alignment = TextAlignmentOptions.Top;

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

        LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRect);
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
