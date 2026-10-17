using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace SimpleDrag;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        // Resolve the private game members up front so a game update fails at load, not mid-drag.
        RuntimeHelpers.RunClassConstructor(typeof(SingleEnemyDragPatch).TypeHandle);
        new Harmony("james.sts2.simple-drag").PatchAll(typeof(ModEntry).Assembly);
    }
}
