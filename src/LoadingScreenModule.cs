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
    private readonly HashSet<int> replacedIndicators = new();
    private Sprite? indicatorSprite;
    private Sprite? sceneImage;
    private GameObject? sceneOverlay;
    private Image? sceneBackground;
    private TMP_Text? sceneTip;
    private float sceneContentChangedAt;
    private Sprite? worldImage;
    private string worldTip = string.Empty;
    private bool worldLoadingWasVisible;
    private bool suppressWorldLoadingUntilHidden;

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
        replacedIndicators.Clear();
        content.Dispose();
        if (indicatorSprite != null)
        {
            if (indicatorSprite.texture != null) UnityEngine.Object.Destroy(indicatorSprite.texture);
            UnityEngine.Object.Destroy(indicatorSprite);
            indicatorSprite = null;
        }
        if (ReferenceEquals(current, this)) current = null;
    }

    private void SetupSceneLoader(SceneLoader loader)
    {
        if (!content.TryNextImage(out sceneImage) || sceneImage == null) return;
        Canvas? canvas = loader.GetComponentInChildren<Canvas>(true) ?? loader.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            log.LogWarning("Valheim SceneLoader canvas was unavailable; leaving the vanilla startup screen unchanged.");
            return;
        }

        sceneOverlay = new GameObject("RagnavikSceneLoading", typeof(RectTransform));
        RectTransform overlayRect = sceneOverlay.GetComponent<RectTransform>();
        overlayRect.SetParent(canvas.transform, false);
        Stretch(overlayRect);
        sceneOverlay.transform.SetAsLastSibling();

        GameObject blackObject = new("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform blackRect = blackObject.GetComponent<RectTransform>();
        blackRect.SetParent(overlayRect, false);
        Stretch(blackRect);
        Image black = blackObject.GetComponent<Image>();
        black.color = Color.black;
        black.raycastTarget = false;

        GameObject imageObject = new("Artwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.SetParent(overlayRect, false);
        Stretch(imageRect);
        sceneBackground = imageObject.GetComponent<Image>();
        sceneBackground.raycastTarget = false;
        ApplyImage(sceneBackground, sceneImage);

        TMP_Text? sourceText = loader.GetComponentInChildren<TMP_Text>(true);
        if (sourceText != null && content.TryNextTip(out string tip))
        {
            sceneTip = UnityEngine.Object.Instantiate(sourceText, overlayRect);
            sceneTip.name = "RagnavikSceneLoadingTip";
            foreach (MonoBehaviour component in sceneTip.GetComponents<MonoBehaviour>())
                if (component != sceneTip) UnityEngine.Object.DestroyImmediate(component);
            SetupTip(sceneTip);
            sceneTip.text = tip;
            sceneTip.gameObject.SetActive(true);
        }

        if (indicatorSprite != null)
        {
            GameObject marker = new("RagnavikSceneLoadingIndicator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FjordGateActivity));
            RectTransform markerRect = marker.GetComponent<RectTransform>();
            markerRect.SetParent(overlayRect, false);
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0f);
            markerRect.pivot = new Vector2(0.5f, 0f);
            markerRect.anchoredPosition = new Vector2(0f, 36f);
            markerRect.sizeDelta = new Vector2(88f, 88f);
            Image markerImage = marker.GetComponent<Image>();
            markerImage.sprite = indicatorSprite;
            markerImage.preserveAspect = true;
            markerImage.raycastTarget = false;
        }

        sceneContentChangedAt = Time.unscaledTime;
        restorations.Add(() =>
        {
            if (sceneOverlay != null) UnityEngine.Object.Destroy(sceneOverlay);
            sceneOverlay = null;
            sceneBackground = null;
            sceneTip = null;
        });
        log.LogInfo("Prepared the Ragnavik initial startup loading screen.");
    }

    private void UpdateSceneLoader(SceneLoader loader)
    {
        if (sceneOverlay == null || sceneBackground == null || sceneImage == null) return;
        sceneOverlay.transform.SetAsLastSibling();
        if (Time.unscaledTime - sceneContentChangedAt >= 10f)
        {
            if (content.TryNextImage(out Sprite? nextImage) && nextImage != null) sceneImage = nextImage;
            if (sceneTip != null && content.TryNextTip(out string nextTip)) sceneTip.text = nextTip;
            sceneContentChangedAt = Time.unscaledTime;
        }
        ApplyImage(sceneBackground, sceneImage);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
            log.LogWarning("Valheim menu loading UI was unavailable; leaving the vanilla screen unchanged.");
            return;
        }

        Sprite? originalSprite = background.sprite;
        bool originalPreserveAspect = background.preserveAspect;
        Color originalColor = background.color;
        if (!content.TryNextImage(out Sprite? image) || image == null) return;

        TMP_Text tip = UnityEngine.Object.Instantiate(sourceText, sourceText.transform.parent);
        tip.name = "RagnavikLoadingTip";
        SetupTip(tip);
        if (content.TryNextTip(out string text)) tip.text = text;
        sourceText.enabled = false;

        ApplyImage(background, image);
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
        log.LogInfo("Prepared the Ragnavik menu transition loading screen.");
    }

    private void SetupWorld(Hud hud)
    {
        worldLoadingWasVisible = false;
        suppressWorldLoadingUntilHidden = false;
        worldImage = null;
        worldTip = string.Empty;
        if (hud.m_loadingImage == null || hud.m_loadingTip == null || hud.m_loadingProgress == null)
        {
            log.LogWarning("Valheim world loading UI was unavailable; leaving the vanilla screen unchanged.");
            return;
        }
        SetupIndicator(hud.m_loadingProgress.transform);
    }

    private void UpdateWorld(Hud hud)
    {
        if (hud.m_loadingScreen == null || hud.m_loadingImage == null || hud.m_loadingTip == null) return;
        bool visible = hud.m_loadingScreen.gameObject.activeInHierarchy && hud.m_loadingImage.gameObject.activeInHierarchy;
        bool teleporting = Player.m_localPlayer != null && Player.m_localPlayer.ShowTeleportAnimation();

        if (teleporting)
        {
            suppressWorldLoadingUntilHidden = true;
            worldLoadingWasVisible = false;
            return;
        }
        if (suppressWorldLoadingUntilHidden)
        {
            if (!visible) suppressWorldLoadingUntilHidden = false;
            return;
        }
        if (!visible)
        {
            worldLoadingWasVisible = false;
            return;
        }

        if (!worldLoadingWasVisible)
        {
            if (content.TryNextImage(out Sprite? nextImage)) worldImage = nextImage;
            if (content.TryNextTip(out string nextTip)) worldTip = nextTip;
        }
        worldLoadingWasVisible = true;

        if (worldImage != null) ApplyImage(hud.m_loadingImage, worldImage);
        if (worldTip.Length > 0) hud.m_loadingTip.text = worldTip;
    }

    private static void SetupTip(TMP_Text tip)
    {
        tip.enableAutoSizing = true;
        tip.fontSizeMin = 16f;
        tip.fontSizeMax = 28f;
        tip.alignment = TextAlignmentOptions.Center;
        tip.rectTransform.anchorMin = new Vector2(0.12f, 0.04f);
        tip.rectTransform.anchorMax = new Vector2(0.88f, 0.22f);
        tip.rectTransform.offsetMin = tip.rectTransform.offsetMax = Vector2.zero;
    }

    private static void ApplyImage(Image target, Sprite image)
    {
        target.sprite = image;
        target.preserveAspect = true;
        target.color = Color.white;
    }

    private void SetupIndicator(Transform root)
    {
        if (indicatorSprite == null || root == null) return;
        foreach (LoadingIndicator indicator in root.GetComponentsInChildren<LoadingIndicator>(true))
        {
            int id = indicator.GetInstanceID();
            if (replacedIndicators.Contains(id)) continue;
            Image? vanillaImage = indicator.GetComponent<Image>() ?? indicator.GetComponentInChildren<Image>(true);
            if (vanillaImage == null) continue;

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
            replacedIndicators.Add(id);
            restorations.Add(() =>
            {
                replacedIndicators.Remove(id);
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
        [HarmonyPatch(typeof(SceneLoader), "Start"), HarmonyPrefix]
        private static void SceneLoaderStart(SceneLoader __instance)
        {
            try { current?.SetupSceneLoader(__instance); }
            catch (Exception error) { current?.log.LogWarning($"Initial startup loading screen fell back to vanilla: {error}"); }
        }

        [HarmonyPatch(typeof(SceneLoader), "Update"), HarmonyPostfix]
        private static void SceneLoaderUpdate(SceneLoader __instance)
        {
            try { current?.UpdateSceneLoader(__instance); }
            catch (Exception error) { current?.log.LogWarning($"Could not maintain initial startup loading content: {error.Message}"); }
        }

        [HarmonyPatch(typeof(FejdStartup), "Awake"), HarmonyPostfix]
        private static void StartupAwake(FejdStartup __instance)
        {
            try { current?.SetupStartup(__instance); }
            catch (Exception error) { current?.log.LogWarning($"Menu loading screen fell back to vanilla: {error}"); }
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
