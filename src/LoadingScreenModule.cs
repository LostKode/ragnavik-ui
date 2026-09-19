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
    private GameObject? sceneIndicator;
    private float sceneContentChangedAt;
    private Sprite? worldImage;
    private GameObject? worldOverlay;
    private Image? worldBackground;
    private TMP_Text? worldTipLabel;
    private GameObject? worldIndicator;
    private GameObject? worldIndicatorLayer;
    private bool worldLoadingWasVisible;
    private bool worldProgressWasActive;
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
        Canvas? canvas = FindSceneCanvas(loader);
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

        TMP_Text? sourceText = canvas.GetComponentInChildren<TMP_Text>(true);
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
            sceneIndicator = new GameObject("RagnavikSceneLoadingIndicator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FjordGateActivity));
            RectTransform markerRect = sceneIndicator.GetComponent<RectTransform>();
            markerRect.SetParent(overlayRect, false);
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.17f);
            markerRect.pivot = new Vector2(0.5f, 0f);
            markerRect.anchoredPosition = new Vector2(0f, 16f);
            markerRect.sizeDelta = new Vector2(88f, 88f);
            Image markerImage = sceneIndicator.GetComponent<Image>();
            markerImage.sprite = indicatorSprite;
            markerImage.preserveAspect = true;
            markerImage.raycastTarget = false;
            sceneIndicator.transform.SetAsLastSibling();
        }

        sceneContentChangedAt = Time.unscaledTime;
        restorations.Add(() =>
        {
            if (sceneOverlay != null) UnityEngine.Object.Destroy(sceneOverlay);
            sceneOverlay = null;
            sceneBackground = null;
            sceneTip = null;
            sceneIndicator = null;
        });
        log.LogInfo("Prepared the Ragnavik initial startup loading screen.");
    }

    private void UpdateSceneLoader(SceneLoader loader)
    {
        if (sceneOverlay == null || sceneBackground == null || sceneImage == null) return;
        sceneOverlay.transform.SetAsLastSibling();
        if (sceneIndicator != null)
        {
            sceneIndicator.SetActive(true);
            sceneIndicator.transform.SetAsLastSibling();
        }
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

    private static Canvas? FindSceneCanvas(SceneLoader loader)
    {
        Canvas? best = null;
        foreach (Canvas candidate in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (candidate == null || candidate.gameObject.scene != loader.gameObject.scene) continue;
            if (best == null || candidate.sortingOrder > best.sortingOrder) best = candidate;
        }
        return best;
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
        if (hud.m_loadingScreen == null || hud.m_loadingTip == null || hud.m_loadingProgress == null)
        {
            log.LogWarning("Valheim world loading UI was unavailable; leaving the vanilla screen unchanged.");
            return;
        }

        Canvas? worldCanvas = hud.m_loadingScreen.GetComponentInParent<Canvas>();
        if (worldCanvas == null)
        {
            log.LogWarning("Valheim world loading canvas was unavailable; leaving the vanilla screen unchanged.");
            return;
        }

        worldOverlay = new GameObject("RagnavikWorldLoading", typeof(RectTransform));
        RectTransform overlayRect = worldOverlay.GetComponent<RectTransform>();
        overlayRect.SetParent(hud.m_loadingScreen.transform, false);
        Stretch(overlayRect);
        worldOverlay.SetActive(false);

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
        worldBackground = imageObject.GetComponent<Image>();
        worldBackground.raycastTarget = false;

        worldTipLabel = UnityEngine.Object.Instantiate(hud.m_loadingTip, overlayRect);
        worldTipLabel.name = "RagnavikWorldLoadingTip";
        foreach (MonoBehaviour component in worldTipLabel.GetComponents<MonoBehaviour>())
            if (component != worldTipLabel) UnityEngine.Object.DestroyImmediate(component);
        SetupTip(worldTipLabel);
        worldTipLabel.gameObject.SetActive(true);

        worldIndicatorLayer = new GameObject("RagnavikWorldLoadingIndicatorLayer", typeof(RectTransform));
        RectTransform indicatorLayerRect = worldIndicatorLayer.GetComponent<RectTransform>();
        indicatorLayerRect.SetParent(worldCanvas.transform, false);
        Stretch(indicatorLayerRect);
        worldIndicatorLayer.SetActive(false);

        if (indicatorSprite != null)
        {
            worldIndicator = new GameObject("RagnavikWorldLoadingIndicator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FjordGateActivity));
            RectTransform markerRect = worldIndicator.GetComponent<RectTransform>();
            markerRect.SetParent(indicatorLayerRect, false);
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.17f);
            markerRect.pivot = new Vector2(0.5f, 0f);
            markerRect.anchoredPosition = new Vector2(0f, 16f);
            markerRect.sizeDelta = new Vector2(88f, 88f);
            Image markerImage = worldIndicator.GetComponent<Image>();
            markerImage.sprite = indicatorSprite;
            markerImage.preserveAspect = true;
            markerImage.raycastTarget = false;
            worldIndicator.transform.SetAsLastSibling();
        }

        restorations.Add(() =>
        {
            if (worldOverlay != null) UnityEngine.Object.Destroy(worldOverlay);
            if (worldIndicatorLayer != null) UnityEngine.Object.Destroy(worldIndicatorLayer);
            worldOverlay = null;
            worldBackground = null;
            worldTipLabel = null;
            worldIndicator = null;
            worldIndicatorLayer = null;
        });
        log.LogInfo("Prepared the Ragnavik full world-entry loading overlay.");
    }

    private void UpdateWorld(Hud hud)
    {
        if (hud.m_loadingScreen == null || hud.m_loadingProgress == null || worldOverlay == null || worldBackground == null) return;
        bool visible = hud.m_loadingScreen.gameObject.activeInHierarchy && hud.m_loadingScreen.alpha > 0.01f;
        bool teleporting = Player.m_localPlayer != null && Player.m_localPlayer.ShowTeleportAnimation();

        if (teleporting)
        {
            suppressWorldLoadingUntilHidden = true;
            worldOverlay.SetActive(false);
            if (worldIndicatorLayer != null) worldIndicatorLayer.SetActive(false);
            worldLoadingWasVisible = false;
            return;
        }
        if (suppressWorldLoadingUntilHidden)
        {
            worldOverlay.SetActive(false);
            if (worldIndicatorLayer != null) worldIndicatorLayer.SetActive(false);
            if (!visible) suppressWorldLoadingUntilHidden = false;
            return;
        }
        if (!visible)
        {
            worldOverlay.SetActive(false);
            if (worldIndicatorLayer != null) worldIndicatorLayer.SetActive(false);
            if (worldLoadingWasVisible) hud.m_loadingProgress.SetActive(worldProgressWasActive);
            worldLoadingWasVisible = false;
            return;
        }

        if (!worldLoadingWasVisible)
        {
            worldProgressWasActive = hud.m_loadingProgress.activeSelf;
            if (content.TryNextImage(out Sprite? nextImage)) worldImage = nextImage;
            if (worldTipLabel != null && content.TryNextTip(out string nextTip)) worldTipLabel.text = nextTip;
        }
        worldLoadingWasVisible = true;
        hud.m_loadingProgress.SetActive(false);
        worldOverlay.SetActive(true);
        worldOverlay.transform.SetAsLastSibling();
        if (worldIndicatorLayer != null)
        {
            worldIndicatorLayer.SetActive(true);
            worldIndicatorLayer.transform.SetAsLastSibling();
        }
        if (worldIndicator != null)
        {
            worldIndicator.SetActive(true);
            worldIndicator.transform.SetAsLastSibling();
        }
        if (worldImage != null) ApplyImage(worldBackground, worldImage);
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
        target.preserveAspect = false;
        target.color = Color.white;
        AspectRatioFitter fitter = target.GetComponent<AspectRatioFitter>() ?? target.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = image.rect.width / image.rect.height;
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
        [HarmonyPatch(typeof(SceneLoader), "Start"), HarmonyPostfix]
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
