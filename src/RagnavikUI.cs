using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RagnavikUI;

[BepInPlugin("lostkode.ragnavik.ui", "Ragnavik UI", "1.0.2")]
public sealed class RagnavikUIPlugin : BaseUnityPlugin
{
    private static ManualLogSource? uiLog;
    private static RectTransform? trackedInventory;
    private static RectTransform? armor;
    private static RectTransform? coin;
    private static RectTransform? weight;
    private static RectTransform? trash;
    private static readonly Vector3[] originalScales = new Vector3[4];
    private static float columnCenterX;
    private static bool loggedMissing;
    private static bool routingTrashClick;

    private void Awake()
    {
        uiLog = Logger;
        Harmony.CreateAndPatchAll(typeof(RagnavikUIPlugin));
    }

    // Other inventory mods can reposition panels after Show. Keep the rows
    // stable after their Update patches.
    [HarmonyPatch(typeof(InventoryGui), "Update")]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void AfterInventoryUpdate(InventoryGui __instance)
    {
        RectTransform? inventory = __instance.m_player.GetComponent<RectTransform>();
        if (inventory == null || !inventory.gameObject.activeInHierarchy ||
            inventory.rect.height <= 0f)
            return;

        if (trackedInventory != inventory || armor == null || coin == null ||
            weight == null || trash == null)
        {
            trackedInventory = inventory;
            armor = FindPanel(inventory, __instance, "Armor");
            coin = FindPanel(inventory, __instance, "CoinPocketUI");
            weight = FindPanel(inventory, __instance, "Weight");
            trash = FindPanel(inventory, __instance, "Trash");
            if (armor == null || coin == null || weight == null || trash == null)
            {
                if (!loggedMissing)
                {
                    uiLog?.LogWarning("Four-row layout waiting for panels: " +
                        $"Armor={armor != null}, CoinPocketUI={coin != null}, " +
                        $"Weight={weight != null}, Trash={trash != null}");
                    loggedMissing = true;
                }
                return;
            }
            loggedMissing = false;
            originalScales[0] = armor.localScale;
            originalScales[1] = coin.localScale;
            originalScales[2] = weight.localScale;
            originalScales[3] = trash.localScale;
        }

        RectTransform[] panels = { armor, coin, weight, trash };
        const float outerMargin = 8f;
        const float rowPadding = 10f;
        float rowPitch = (inventory.rect.height - 2f * outerMargin) / 4f;
        if (rowPitch <= 0f)
            return;

        // Every panel must fit within one quarter row. A minimum scale clamp
        // would make this no-overlap condition false on smaller inventories.
        float tallest = 0f;
        float widest = 0f;
        for (int i = 0; i < panels.Length; i++)
        {
            panels[i].localScale = originalScales[i];
            Bounds bounds = PanelBounds(inventory, panels[i]);
            tallest = Mathf.Max(tallest, bounds.size.y);
            widest = Mathf.Max(widest, bounds.size.x);
        }
        float fit = Mathf.Min(1f, rowPitch / Mathf.Max(1f, tallest + rowPadding));
        // Place the sidebar outside the inventory with a real gutter. The old
        // armor position put the trash drop target against the item slots.
        columnCenterX = inventory.rect.xMax + 4f + widest * fit * 0.5f;

        for (int i = 0; i < panels.Length; i++)
        {
            panels[i].localScale = originalScales[i] * fit;
            float rowCenterY = inventory.rect.yMax - outerMargin -
                (i + 0.5f) * rowPitch;
            if (i == 2) rowCenterY -= 8f;
            if (i == 3) rowCenterY -= 16f;
            Bounds current = PanelBounds(inventory, panels[i]);
            Vector3 delta = inventory.TransformVector(new Vector3(
                columnCenterX - current.center.x,
                rowCenterY - current.center.y, 0f));
            panels[i].position += delta;
        }
        // TrashItems adds a separate transparent ButtonCanvas as its click
        // target. Keep it centered over the visible trash panel after moving
        // the parent, and leave the mod's own click listener intact.
        RectTransform? button = trash.Find("ButtonCanvas") as RectTransform;
        if (button != null)
        {
            Bounds visible = PanelBounds(inventory, trash);
            button.position = inventory.TransformPoint(visible.center);
            button.sizeDelta = trash.rect.size;
        }
    }

    // TrashItems' transparent ButtonCanvas can miss the mouse even while its
    // visible panel is under the cursor. Route that exact click through its
    // existing Button listener rather than duplicating deletion logic.
    [HarmonyPatch(typeof(InventoryGui), "UpdateItemDrag")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void BeforeItemDragUpdate(InventoryGui __instance)
    {
        if (routingTrashClick || trash == null || !trash.gameObject.activeInHierarchy ||
            !Input.GetMouseButtonDown(0) ||
            AccessTools.Field(typeof(InventoryGui), "m_dragItem")?.GetValue(__instance) == null)
            return;

        Canvas? canvas = trash.GetComponentInParent<Canvas>();
        Camera? camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        if (!RectTransformUtility.RectangleContainsScreenPoint(trash, Input.mousePosition, camera))
            return;

        Button? button = trash.Find("ButtonCanvas")?.GetComponent<Button>();
        if (button == null)
            return;

        routingTrashClick = true;
        try
        {
            uiLog?.LogInfo("Routing held-item click through TrashItems button");
            button.onClick.Invoke();
        }
        finally
        {
            routingTrashClick = false;
        }
    }

    private static RectTransform? FindPanel(RectTransform playerInventory,
        InventoryGui gui, string panelName)
    {
        foreach (RectTransform panel in playerInventory.GetComponentsInChildren<RectTransform>(true))
            if (panel.name == panelName)
                return panel;
        foreach (RectTransform panel in gui.GetComponentsInChildren<RectTransform>(true))
            if (panel.name == panelName)
                return panel;
        return null;
    }

    private static Bounds PanelBounds(RectTransform inventory, RectTransform panel)
    {
        Vector3[] corners = new Vector3[4];
        panel.GetWorldCorners(corners);
        Bounds bounds = new Bounds(inventory.InverseTransformPoint(corners[0]),
            Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            bounds.Encapsulate(inventory.InverseTransformPoint(corners[i]));
        return bounds;
    }
}
