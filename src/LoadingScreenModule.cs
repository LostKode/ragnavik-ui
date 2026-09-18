using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RagnavikUI;

internal sealed class LoadingScreenModule
{
    private static LoadingScreenModule? current;
    private readonly Harmony harmony = new(RagnavikUIPlugin.PluginGuid + ".loading");
    private readonly ManualLogSource log;
    private readonly LoadingContent content;
    private readonly List<Action> restorations = new();
    private Sprite? indicatorSprite;
    private bool worldLoadingWasVisible;

    internal LoadingScreenModule(BaseUnityPlugin plugin, ManualLogSource log)
    {
        this.log = log;
        string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
        content = new LoadingContent(pluginDirectory, log);
        indicatorSprite = LoadSprite(Path.Combine(pluginDirectory, "ragnavik-fjord-gate.png"), "RagnavikLoadingIndicator");
    }

    internal void Start()
    {
        current = this;
        harmony.PatchAll(typeof(Patches));
    }

    internal void Stop()
    {
        harmony.UnpatchSelf();
        for (int index = restorations.Count - 1; index >= 0; index--)
        {
            try { restorations[index](); }
            catch (Exception error) { log.LogDebug($"Could not restore a loading UI object: {error.Message}"); }
        }
        restorations.Clear();
        content.Dispose();
        if (indicatorSprite != null)
        {
            if (indicatorSprite.texture != null) UnityEngine.Object.Destroy(indicatorSprite.texture);
            UnityEngine.Object.Destroy(indicatorSprite);
            indicatorSprite = null;
        }
        if (ReferenceEquals(current, this)) current = null;
    }

    private void SetupStartup(FejdStartup startup)
    {
        if (startup.m_loading == null) return;
        Transform? backgroundTransform = startup.m_loading.transform.Find("Bkg");
        Transform? textTransform = startup.m_loading.transform.Find("Text");
        Image? background = backgroundTransform?.GetComponent<Image>();
        TMP_Text? sourceText = textTransform?.GetComponent<TMP_Text>();
        if (background == null || sourceText == null)
        {
            log.LogWarning("Valheim startup loading UI was unavailable; leaving the vanilla screen unchanged.");
            return;
        }

        Sprite? originalSprite = background.sprite;
        bool originalPreserveAspect = background.preserveAspect;
        Color originalColor = background.color;
        if (!content.TryNextImage(out Sprite? image) || image == null) return;

        TMP_Text tip = UnityEngine.Object.Instantiate(sourceText, sourceText.transform.parent);
        tip.name = "RagnavikLoadingTip";
        tip.enableAutoSizing = true;
        tip.fontSizeMin = 16f;
        tip.fontSizeMax = 28f;
        tip.alignment = TextAlignmentOptions.Center;
        tip.rectTransform.anchorMin = new Vector2(0.12f, 0.04f);
        tip.rectTransform.anchorMax = new Vector2(0.88f, 0.22f);
        tip.rectTransform.offsetMin = tip.rectTransform.offsetMax = Vector2.zero;
        if (content.TryNextTip(out string text)) tip.text = text;
        sourceText.enabled = false;

        background.sprite = image;
        background.preserveAspect = true;
        background.color = Color.white;
        SetupIndicator(startup.m_loading.transform);

        restorations.Add(() =>
        {
            if (background != null)
            {
                background.sprite = originalSprite;
                background.preserveAspect = originalPreserveAspect;
                background.color = originalColor;
            }
            if (sourceText != null) sourceText.enabled = true;
            if (tip != null) UnityEngine.Object.Destroy(tip.gameObject);
        });
        log.LogInfo("Prepared the Ragnavik startup loading screen.");
    }

    private void SetupWorld(Hud hud)
    {
        if (hud.m_loadingImage == null || hud.m_loadingTip == null)
        {
            log.LogWarning("Valheim world loading UI was unavailable; leaving the vanilla screen unchanged.");
            return;
        }
        hud.m_loadingImage.preserveAspect = true;
        worldLoadingWasVisible = false;
    }

    private void UpdateWorld(Hud hud)
    {
        if (hud.m_loadingScreen == null || hud.m_loadingImage == null || hud.m_loadingTip == null) return;
        bool visible = hud.m_loadingScreen.gameObject.activeInHierarchy && hud.m_loadingImage.gameObject.activeInHierarchy;
        bool teleporting = Player.m_localPlayer != null && Player.m_localPlayer.ShowTeleportAnimation();
        if (teleporting) return;
        if (visible && !worldLoadingWasVisible)
        {
            if (content.TryNextImage(out Sprite? image) && image != null)
            {
                hud.m_loadingImage.sprite = image;
                hud.m_loadingImage.preserveAspect = true;
                hud.m_loadingImage.color = Color.white;
            }
            if (content.TryNextTip(out string tip)) hud.m_loadingTip.text = tip;
        }
        worldLoadingWasVisible = visible;
    }

    private void SetupIndicator(Transform root)
    {
        if (indicatorSprite == null || root == null) return;
        foreach (LoadingIndicator indicator in root.GetComponentsInChildren<LoadingIndicator>(true))
        {
            Image? vanillaImage = indicator.GetComponent<Image>() ?? indicator.GetComponentInChildren<Image>(true);
            if (vanillaImage == null || vanillaImage.gameObject.name.StartsWith("Ragnavik", StringComparison.Ordinal)) continue;

            GameObject marker = new("RagnavikLoadingIndicator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FjordGateActivity));
            RectTransform rect = marker.GetComponent<RectTransform>();
            rect.SetParent(vanillaImage.rectTransform.parent, false);
            rect.anchorMin = vanillaImage.rectTransform.anchorMin;
            rect.anchorMax = vanillaImage.rectTransform.anchorMax;
            rect.pivot = vanillaImage.rectTransform.pivot;
            rect.anchoredPosition = vanillaImage.rectTransform.anchoredPosition;
            rect.sizeDelta = vanillaImage.rectTransform.sizeDelta;
            Image markerImage = marker.GetComponent<Image>();
            markerImage.sprite = indicatorSprite;
            markerImage.preserveAspect = true;
            markerImage.raycastTarget = false;
            bool vanillaEnabled = vanillaImage.enabled;
            vanillaImage.enabled = false;
            restorations.Add(() =>
            {
                if (vanillaImage != null) vanillaImage.enabled = vanillaEnabled;
                if (marker != null) UnityEngine.Object.Destroy(marker);
            });
        }
    }

    private Sprite? LoadSprite(string path, string name)
    {
        if (!File.Exists(path)) return null;
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
            log.LogWarning($"Could not load the Fjord Gate loading indicator: {error.Message}");
            return null;
        }
    }

    private static class Patches
    {
        [HarmonyPatch(typeof(FejdStartup), "Awake"), HarmonyPostfix]
        private static void StartupAwake(FejdStartup __instance)
        {
            try { current?.SetupStartup(__instance); }
            catch (Exception error) { current?.log.LogWarning($"Startup loading screen fell back to vanilla: {error}"); }
        }

        [HarmonyPatch(typeof(Hud), "Awake"), HarmonyPostfix]
        private static void HudAwake(Hud __instance)
        {
            try { current?.SetupWorld(__instance); }
            catch (Exception error) { current?.log.LogWarning($"World loading screen fell back to vanilla: {error}"); }
        }

        [HarmonyPatch(typeof(Hud), "UpdateBlackScreen"), HarmonyPostfix]
        private static void HudUpdateBlackScreen(Hud __instance)
        {
            try { current?.UpdateWorld(__instance); }
            catch (Exception error) { current?.log.LogWarning($"Could not update world loading content: {error.Message}"); }
        }
    }
}

internal sealed class FjordGateActivity : MonoBehaviour
{
    private RectTransform? rect;
    private CanvasGroup? canvasGroup;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void Update()
    {
        float pulse = 0.92f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.08f;
        if (rect != null) rect.localScale = Vector3.one * pulse;
        if (canvasGroup != null) canvasGroup.alpha = 0.78f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.14f;
    }
}
