using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

if (args.Length != 2)
{
    throw new ArgumentException("Usage: Compatibility <game-data-directory> <SimpleDrag.dll>");
}

var gameData = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(gameData, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};

var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
var patch = mod.GetType("SimpleDrag.SingleEnemyDragPatch", throwOnError: true)!;
RuntimeHelpers.RunClassConstructor(patch.TypeHandle);
RuntimeHelpers.RunClassConstructor(mod.GetType("SimpleDrag.DragDiagnostics", throwOnError: true)!.TypeHandle);

var harmonyAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameData, "0Harmony.dll"));
var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
var harmony = Activator.CreateInstance(harmonyType, "simple-drag.compatibility-check")!;
harmonyType.GetMethod("PatchAll", [typeof(Assembly)])!.Invoke(harmony, [mod]);
var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameData, "sts2.dll"));
var targets = new[]
{
    ("MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay", "SingleCreatureTargeting"),
    ("MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay", "ShowMultiCreatureTargetingVisuals"),
    ("MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay", "Start"),
    ("MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay", "_Input"),
    ("MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay", "_ExitTree"),
    ("MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay", "CancelPlayCard"),
    ("MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay", "TryPlayCard"),
    ("MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay", "Cleanup")
};
foreach (var (typeName, methodName) in targets)
{
    var type = game.GetType(typeName, throwOnError: true)!;
    var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
    var info = harmonyType.GetMethod("GetPatchInfo")!.Invoke(null, [method]);
    if (info == null)
    {
        throw new InvalidOperationException($"Harmony did not patch {typeName}.{methodName}.");
    }
}
var manager = game.GetType("MegaCrit.Sts2.Core.Nodes.Combat.NTargetManager", throwOnError: true)!;
var cardModel = game.GetType("MegaCrit.Sts2.Core.Models.CardModel", throwOnError: true)!;
var canPlay = cardModel.GetMethods().Single(method => method.Name == "CanPlay" && method.GetParameters().Length == 2);
if (harmonyType.GetMethod("GetPatchInfo")!.Invoke(null, [canPlay]) == null)
{
    throw new InvalidOperationException("Playability rejection diagnostics were not installed.");
}
var input = manager.GetMethod("_Input", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
if (harmonyType.GetMethod("GetPatchInfo")!.Invoke(null, [input]) != null)
{
    throw new InvalidOperationException("The obsolete release-only input patch is still installed.");
}
Console.WriteLine("PASS: Private bindings and all drag/diagnostic patches work against the installed game; target-manager input remains untouched.");
