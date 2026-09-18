using BepInEx;

namespace RagnavikUI;

[BepInPlugin("lostkode.ragnavik.ui", "Ragnavik UI", "1.0.4")]
public sealed class RagnavikUIPlugin : BaseUnityPlugin
{
    // Inventory controls now retain their owning mods' native placement.
    // This package remains installed for the shared compass and clock presentation.
}
