using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RagnavikUI;

internal sealed class LoadingContent : IDisposable
{
    private readonly ManualLogSource log;
    private readonly System.Random random = new();
    private readonly List<Sprite> images = new();
    private readonly List<string> tips = new();
    private int lastImage = -1;
    private int lastTip = -1;

    internal LoadingContent(string pluginDirectory, ManualLogSource log)
    {
        this.log = log;
        string directory = Path.Combine(pluginDirectory, "loading");
        LoadImages(directory);
        LoadTips(Path.Combine(directory, "tips.txt"));
        log.LogInfo($"Loaded {images.Count} Ragnavik loading images and {tips.Count} tips.");
    }

    internal bool TryNextImage(out Sprite? sprite)
    {
        sprite = null;
        if (images.Count == 0) return false;
        lastImage = NextIndex(images.Count, lastImage);
        sprite = images[lastImage];
        return true;
    }

    internal bool TryNextTip(out string tip)
    {
        tip = string.Empty;
        if (tips.Count == 0) return false;
        lastTip = NextIndex(tips.Count, lastTip);
        tip = tips[lastTip];
        return true;
    }

    public void Dispose()
    {
        foreach (Sprite sprite in images)
        {
            if (sprite != null && sprite.texture != null) UnityEngine.Object.Destroy(sprite.texture);
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
        }
        images.Clear();
        tips.Clear();
    }

    private int NextIndex(int count, int previous)
    {
        if (count == 1) return 0;
        int index = random.Next(count - 1);
        return index >= previous ? index + 1 : index;
    }

    private void LoadImages(string directory)
    {
        if (!Directory.Exists(directory))
        {
            log.LogWarning($"Loading content was not found at {directory}; vanilla screens remain available.");
            return;
        }
        foreach (string path in Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly))
        {
            try
            {
                Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
                {
                    UnityEngine.Object.Destroy(texture);
                    log.LogWarning($"Could not decode {Path.GetFileName(path)}.");
                    continue;
                }
                texture.name = "RagnavikLoading_" + Path.GetFileNameWithoutExtension(path);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                images.Add(Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f));
            }
            catch (Exception error) { log.LogWarning($"Could not load {Path.GetFileName(path)}: {error.Message}"); }
        }
    }

    private void LoadTips(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                string tip = line.Trim();
                if (tip.Length > 0) tips.Add(tip);
            }
        }
        catch (Exception error) { log.LogWarning($"Could not load loading tips: {error.Message}"); }
    }
}
