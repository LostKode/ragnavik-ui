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
    private static DirectEntryModule? current;
    private readonly Harmony harmony = new(RagnavikUIPlugin.PluginGuid + ".direct-entry");
    private readonly ManualLogSource log;
    private readonly DirectEntryTarget target;

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

    private void RecoverFromFailure(FejdStartup startup)
    {
        if (target.IsTest) return;
        if (!startup.m_connectionFailedPanel.activeSelf) return;
        startup.m_serverListPanel.SetActive(false);
        startup.m_startGamePanel.SetActive(false);
        startup.m_characterSelectScreen.SetActive(false);
        startup.m_mainMenu.SetActive(true);
        string nativeMessage = startup.m_connectionFailedError.text;
        startup.m_connectionFailedError.text = $"Could not connect to {target.DisplayName}.\n\n{nativeMessage}\n\nReturn to the main menu and choose {target.PlayLabel} to retry.";
        log.LogWarning($"Direct entry to {target.DisplayName} failed. Target details were not logged.");
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
        private static void RecoverConnectionFailure(FejdStartup __instance) => current?.RecoverFromFailure(__instance);
    }
}
