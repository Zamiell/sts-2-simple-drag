using MegaCrit.Sts2.Core.Logging;

namespace SimpleDrag;

// One line per drag decision so intermittent arrow fallbacks can be diagnosed from godot.log.
internal static class DragLog
{
    internal static void Info(string message) => Log.Info($"[SimpleDrag] {message}");
}
