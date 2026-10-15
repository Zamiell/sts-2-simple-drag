using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace SimpleDrag;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        RuntimeHelpers.RunClassConstructor(typeof(SingleEnemyDragPatch).TypeHandle);
        new Harmony("james.sts2.simple-drag").PatchAll(typeof(ModEntry).Assembly);
        Log.Info("[SimpleDrag] Single-enemy attack drag targeting enabled.");
    }
}
