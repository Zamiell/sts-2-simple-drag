using System.Diagnostics;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SimpleDrag;

internal static class DragDiagnostics
{
    private sealed class Attempt
    {
        public int Id { get; } = Interlocked.Increment(ref nextId);
        public Stopwatch Elapsed { get; } = Stopwatch.StartNew();
        public string? LastRejection { get; set; }
    }

    private static int nextId;
    private static readonly ConditionalWeakTable<NCardPlay, Attempt> Attempts = new();
    private static readonly ConditionalWeakTable<CardModel, Attempt> CardAttempts = new();
    private static readonly AccessTools.FieldRef<NMouseCardPlay, bool> LeftDown =
        AccessTools.FieldRefAccess<NMouseCardPlay, bool>("_isLeftMouseDown");
    private static readonly AccessTools.FieldRef<NMouseCardPlay, bool> Shortcut =
        AccessTools.FieldRefAccess<NMouseCardPlay, bool>("_skipStartCardDrag");
    private static readonly Func<NMouseCardPlay, float> PlayThreshold =
        AccessTools.MethodDelegate<Func<NMouseCardPlay, float>>(
            AccessTools.DeclaredPropertyGetter(typeof(NMouseCardPlay), "PlayZoneThreshold"));
    private static readonly Func<NMouseCardPlay, float> CancelThreshold =
        AccessTools.MethodDelegate<Func<NMouseCardPlay, float>>(
            AccessTools.DeclaredPropertyGetter(typeof(NMouseCardPlay), "CancelZoneThreshold"));

    internal static void Record(NCardPlay play, string message)
    {
        if (play is not NMouseCardPlay mouse)
        {
            return;
        }
        var attempt = Attempts.GetValue(play, _ => new Attempt());
        var card = play.Holder.CardModel;
        if (card != null)
        {
            CardAttempts.Remove(card);
            CardAttempts.Add(card, attempt);
        }
        var enemies = card?.CombatState == null ? "no-combat"
            : string.Join(", ", card.CombatState.Enemies.Select(Describe));
        var viewport = play.GetViewport();
        var position = viewport.GetMousePosition();
        Log.Info(FormattableString.Invariant(
            $"[SimpleDrag] utc={DateTime.UtcNow:O} drag={attempt.Id} elapsedMs={attempt.Elapsed.ElapsedMilliseconds} event={message} | card={card?.Id} type={card?.Type} targetType={card?.TargetType} pile={card?.Pile?.Type} energy={card?.Owner.PlayerCombatState?.Energy} mouse=({position.X:F1},{position.Y:F1}) playY={PlayThreshold(mouse):F1} cancelY={CancelThreshold(mouse):F1} leftDown={LeftDown(mouse)} shortcut={Shortcut(mouse)} enemies=[{enemies}]"));
    }

    internal static void RecordRejectedCard(CardModel card, UnplayableReason reason, AbstractModel? preventer)
    {
        if (CardAttempts.TryGetValue(card, out var attempt))
        {
            var message = $"can-play-rejected card={card.Id} reason={reason} preventer={preventer?.GetType().Name ?? "none"}";
            // CanPlay is also queried by UI refreshes. Keep repeated identical failures out of the log.
            if (attempt.LastRejection != message)
            {
                attempt.LastRejection = message;
                Log.Info($"[SimpleDrag] utc={DateTime.UtcNow:O} drag={attempt.Id} elapsedMs={attempt.Elapsed.ElapsedMilliseconds} event={message}");
            }
        }
    }

    internal static void StopTrackingCard(NCardPlay play)
    {
        if (play.Holder.CardModel is { } card)
        {
            CardAttempts.Remove(card);
        }
    }

    internal static string Describe(Creature? creature) => creature == null ? "none"
        : $"{creature.ModelId}#{creature.CombatId}:hp={creature.CurrentHp}:alive={creature.IsAlive}:hittable={creature.IsHittable}";
}

[HarmonyPatch(typeof(NMouseCardPlay), nameof(NMouseCardPlay.Start))]
internal static class DragStartDiagnostics
{
    [HarmonyPrefix]
    internal static void Prefix(NMouseCardPlay __instance) => DragDiagnostics.Record(__instance, "start-before-input-initialization");
}

[HarmonyPatch(typeof(NMouseCardPlay), nameof(NMouseCardPlay._Input))]
internal static class DragInputDiagnostics
{
    [HarmonyPrefix]
    internal static void Prefix(NMouseCardPlay __instance, InputEvent inputEvent)
    {
        // Motion events are deliberately excluded: log only input transitions.
        if (inputEvent is InputEventMouseButton mouse)
        {
            DragDiagnostics.Record(__instance, $"input button={mouse.ButtonIndex} pressed={mouse.Pressed}");
        }
        else if (inputEvent is InputEventKey { Pressed: true, Echo: false } key)
        {
            DragDiagnostics.Record(__instance, $"input key={key.Keycode}");
        }
    }
}

[HarmonyPatch(typeof(NCardPlay), nameof(NCardPlay.CancelPlayCard))]
internal static class DragCancelDiagnostics
{
    [HarmonyPrefix]
    internal static void Prefix(NCardPlay __instance)
    {
        if (__instance is NMouseCardPlay)
        {
            var callers = string.Join(" <- ", new StackTrace(2, false).GetFrames().Take(4)
                .Select(frame => $"{frame.GetMethod()?.DeclaringType?.Name}.{frame.GetMethod()?.Name}"));
            DragDiagnostics.Record(__instance, $"cancel-request callers={callers}");
        }
    }
}

[HarmonyPatch(typeof(NCardPlay), "TryPlayCard")]
internal static class DragPlayDiagnostics
{
    [HarmonyPrefix]
    internal static void Prefix(NCardPlay __instance, Creature? target) =>
        DragDiagnostics.Record(__instance, $"try-play target={DragDiagnostics.Describe(target)}");
}

[HarmonyPatch(typeof(NCardPlay), "Cleanup")]
internal static class DragFinishDiagnostics
{
    [HarmonyPrefix]
    internal static void Prefix(NCardPlay __instance, bool isFinished) =>
        DragDiagnostics.Record(__instance, $"cleanup acceptedByGame={isFinished}");
}

[HarmonyPatch(typeof(NMouseCardPlay), nameof(NMouseCardPlay._ExitTree))]
internal static class DragExitDiagnostics
{
    [HarmonyPrefix]
    internal static void Prefix(NMouseCardPlay __instance)
    {
        DragDiagnostics.Record(__instance, "exit-tree");
        DragDiagnostics.StopTrackingCard(__instance);
    }
}

[HarmonyPatch]
internal static class CardPlayabilityDiagnostics
{
    [HarmonyTargetMethod]
    internal static System.Reflection.MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(CardModel), nameof(CardModel.CanPlay),
            [typeof(UnplayableReason).MakeByRefType(), typeof(AbstractModel).MakeByRefType()]);

    [HarmonyPostfix]
    internal static void Postfix(CardModel __instance, bool __result, UnplayableReason reason, AbstractModel? preventer)
    {
        if (!__result)
        {
            DragDiagnostics.RecordRejectedCard(__instance, reason, preventer);
        }
    }
}
