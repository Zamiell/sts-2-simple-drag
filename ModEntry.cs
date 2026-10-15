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
        RuntimeHelpers.RunClassConstructor(typeof(DragDiagnostics).TypeHandle);
        new Harmony("james.sts2.simple-drag").PatchAll(typeof(ModEntry).Assembly);
        Log.Info($"[SimpleDrag] v{typeof(ModEntry).Assembly.GetName().Version} diagnostics enabled; gameAssembly={typeof(MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay).Assembly.ManifestModule.ModuleVersionId}. Lifecycle/input events only; no per-frame logging.");
    }
}
