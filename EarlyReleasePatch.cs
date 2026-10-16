using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SimpleDrag;

[HarmonyPatch(typeof(NMouseCardPlay), nameof(NMouseCardPlay._Input))]
internal static class EarlyReleasePatch
{
    [HarmonyPrefix]
    internal static void Prefix(NMouseCardPlay __instance, InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            SingleEnemyDragPatch.RecordLeftRelease(__instance);
        }
    }
}
