using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SimpleDrag;

[HarmonyPatch(typeof(NMouseCardPlay), "SingleCreatureTargeting")]
internal static class SingleEnemyDragPatch
{
    private static readonly HashSet<NCardPlay> ActiveDrags = new(ReferenceEqualityComparer.Instance);

    // Plays whose latest left-button release happened above the play-zone line.
    private static readonly ConditionalWeakTable<NMouseCardPlay, object> ReleasedInPlayZone = new();

    private static readonly AccessTools.FieldRef<NMouseCardPlay, Creature?> SelectedTarget =
        AccessTools.FieldRefAccess<NMouseCardPlay, Creature?>("_target");

    private static readonly AccessTools.FieldRef<NMouseCardPlay, bool> StartedWithShortcut =
        AccessTools.FieldRefAccess<NMouseCardPlay, bool>("_skipStartCardDrag");

    private static readonly AccessTools.FieldRef<NMouseCardPlay, CancellationTokenSource> Cancellation =
        AccessTools.FieldRefAccess<NMouseCardPlay, CancellationTokenSource>("_cancellationTokenSource");

    private static readonly Func<NMouseCardPlay, bool> IsCardInPlayZone =
        AccessTools.MethodDelegate<Func<NMouseCardPlay, bool>>(
            AccessTools.DeclaredMethod(typeof(NMouseCardPlay), "IsCardInPlayZone"));

    private static readonly Func<NMouseCardPlay, TargetMode, Task> MultiCreatureTargeting =
        AccessTools.MethodDelegate<Func<NMouseCardPlay, TargetMode, Task>>(
            AccessTools.DeclaredMethod(typeof(NMouseCardPlay), "MultiCreatureTargeting"));

    [HarmonyPrefix]
    internal static bool Prefix(
        NMouseCardPlay __instance,
        TargetMode targetMode,
        TargetType targetType,
        ref Task __result)
    {
        // The game picks click targeting when the button is already up once the card reaches the play zone.
        // A fast flick can release above the line before that frame's check, so treat it as a drag release.
        var requestedMode = targetMode;
        if (targetMode == TargetMode.ClickMouseToTarget && !StartedWithShortcut(__instance) &&
            ReleasedInPlayZone.TryGetValue(__instance, out _))
        {
            targetMode = TargetMode.ReleaseMouseToTarget;
        }

        var reason = targetMode != TargetMode.ReleaseMouseToTarget ? $"mode-{targetMode}"
            : targetType != TargetType.AnyEnemy ? $"selection-type-{targetType}"
            : StartedWithShortcut(__instance) ? "keyboard-shortcut"
            : Cancellation(__instance).IsCancellationRequested ? "already-cancelled"
            : null;
        if (reason == null)
        {
            GetOnlyTarget(__instance, out reason);
        }
        DragDiagnostics.Record(__instance,
            $"route mode={requestedMode} effectiveMode={targetMode} decision={(reason == "eligible" ? "simple-drag" : "vanilla")} reason={reason}");
        if (reason != "eligible")
        {
            return true;
        }

        __result = DragWithoutArrow(__instance, targetMode);
        return false;
    }

    internal static void RecordLeftRelease(NMouseCardPlay play)
    {
        if (IsCardInPlayZone(play))
        {
            ReleasedInPlayZone.AddOrUpdate(play, play);
        }
        else
        {
            ReleasedInPlayZone.Remove(play);
        }
    }

    private static async Task DragWithoutArrow(NMouseCardPlay play, TargetMode targetMode)
    {
        ActiveDrags.Add(play);
        try
        {
            // Reuse the game's card-following, release and cancellation behavior.
            // Unlike SingleCreatureTargeting, this never centers the card or starts an arrow.
            await MultiCreatureTargeting(play, targetMode);
            if (Cancellation(play).IsCancellationRequested)
            {
                DragDiagnostics.Record(play, "drag-ended cancellation-requested");
                return;
            }

            var target = GetOnlyTarget(play, out var reason);
            var inPlayZone = IsCardInPlayZone(play);
            DragDiagnostics.Record(play, $"release-validation inPlayZone={inPlayZone} target={reason}");
            if (!inPlayZone || target == null)
            {
                play.CancelPlayCard();
                return;
            }

            // StartAsync resumes the normal TryPlayCard path after this task completes.
            SelectedTarget(play) = target.Entity;
            DragDiagnostics.Record(play, "target-selected; returning to game play validation");
        }
        catch (Exception exception)
        {
            DragDiagnostics.Record(play, $"drag-exception {exception}");
            throw;
        }
        finally
        {
            ActiveDrags.Remove(play);
        }
    }

    internal static void ShowPreview(NCardPlay play)
    {
        if (!ActiveDrags.Contains(play) || play.Holder.CardNode is not { } cardNode ||
            GetOnlyTarget(play, out _) is not { } target)
        {
            return;
        }

        cardNode.SetPreviewTarget(target.Entity);
        cardNode.UpdateVisuals((cardNode.Model?.Pile?.Type).GetValueOrDefault(), CardPreviewMode.MultiCreatureTargeting);
        target.ShowMultiselectReticle();
    }

    private static NCreature? GetOnlyTarget(NCardPlay play, out string reason)
    {
        var card = play.Holder.CardModel;
        reason = card == null ? "missing-card"
            : card.Type is not (CardType.Attack or CardType.Skill) ? $"card-type-{card.Type}"
            : card.TargetType != TargetType.AnyEnemy ? $"card-target-type-{card.TargetType}"
            : card.CombatState == null ? "missing-combat"
            : "eligible";
        if (card?.CombatState == null || reason != "eligible")
        {
            return null;
        }

        Creature? target = null;
        foreach (var enemy in card.CombatState.Enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }
            if (target != null)
            {
                reason = "multiple-living-enemies";
                return null;
            }
            target = enemy;
        }

        reason = target == null ? "no-living-enemies"
            : !target.IsHittable ? "enemy-not-hittable"
            : !card.IsValidTarget(target) ? "invalid-card-target"
            : "eligible";
        if (target == null || reason != "eligible")
        {
            return null;
        }
        if (!Hook.ShouldAllowTargeting(card.CombatState, target, out var preventer))
        {
            reason = $"targeting-blocked-by-{preventer?.GetType().Name ?? "unknown"}";
            return null;
        }

        var node = NCombatRoom.Instance?.GetCreatureNode(target);
        reason = node == null ? "missing-enemy-node" : "eligible";
        return node;
    }
}

[HarmonyPatch(typeof(NCardPlay), "ShowMultiCreatureTargetingVisuals")]
internal static class SimpleDragVisualsPatch
{
    [HarmonyPostfix]
    internal static void Postfix(NCardPlay __instance) => SingleEnemyDragPatch.ShowPreview(__instance);
}
