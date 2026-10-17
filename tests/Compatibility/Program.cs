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

var harmonyAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameData, "0Harmony.dll"));
var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
var harmony = Activator.CreateInstance(harmonyType, "simple-drag.compatibility-check")!;
harmonyType.GetMethod("PatchAll", [typeof(Assembly)])!.Invoke(harmony, [mod]);
var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameData, "sts2.dll"));
var targets = new[]
{
    ("MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay", "SingleCreatureTargeting"),
    ("MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay", "ShowMultiCreatureTargetingVisuals"),
    ("MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay", "_Input")
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
Console.WriteLine("PASS: Private bindings and all drag patches work against the installed game.");
