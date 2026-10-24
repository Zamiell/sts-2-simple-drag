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
        var card = __instance.Holder.CardModel;
        var releasedInPlayZone = ReleasedInPlayZone.TryGetValue(__instance, out _);
        string reason;
        if (StartedWithShortcut(__instance))
        {
            reason = "keyboard-shortcut";
        }
        else if (targetType != TargetType.AnyEnemy)
        {
            reason = $"selection-type-{targetType}";
        }
        else if (Cancellation(__instance).IsCancellationRequested)
        {
            reason = "already-cancelled";
        }
        else
        {
            // The game picks click targeting when the button is already up once the card reaches the play zone.
            // A fast flick can release above the line before that frame's check, so treat it as a drag release.
            if (targetMode == TargetMode.ClickMouseToTarget && releasedInPlayZone)
            {
                targetMode = TargetMode.ReleaseMouseToTarget;
            }
            if (targetMode != TargetMode.ReleaseMouseToTarget)
            {
                reason = $"mode-{targetMode}";
            }
            else
            {
                GetOnlyTarget(__instance, out reason);
            }
        }

        var simplified = reason == Eligible;
        DragLog.Info($"route card={card?.Id} mode={targetMode} releasedInPlayZone={releasedInPlayZone} " +
            $"decision={(simplified ? "simple-drag" : "vanilla")} reason={reason}");
        if (!simplified)
        {
            return true;
        }

        __result = DragWithoutArrow(__instance, targetMode);
        return false;
    }

    internal static void RecordLeftRelease(NMouseCardPlay play)
    {
        var inPlayZone = IsCardInPlayZone(play);
        DragLog.Info($"left-release card={play.Holder.CardModel?.Id} inPlayZone={inPlayZone}");
        if (inPlayZone)
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
                DragLog.Info("drag-ended cancelled");
                return;
            }

            var inPlayZone = IsCardInPlayZone(play);
            var target = GetOnlyTarget(play, out var reason);
            DragLog.Info($"drag-ended inPlayZone={inPlayZone} target={reason}");
            if (!inPlayZone || target == null)
            {
                play.CancelPlayCard();
                return;
            }

            // StartAsync resumes the normal TryPlayCard path after this task completes.
            SelectedTarget(play) = target.Entity;
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

    private const string Eligible = "eligible";

    private static NCreature? GetOnlyTarget(NCardPlay play, out string reason)
    {
        var card = play.Holder.CardModel;
        if (card?.CombatState == null)
        {
            reason = card == null ? "no-card" : "no-combat";
            return null;
        }
        if (card.Type is not (CardType.Attack or CardType.Skill) || card.TargetType != TargetType.AnyEnemy)
        {
            reason = $"card-{card.Type}-{card.TargetType}";
            return null;
        }

        Creature? target = null;
        var living = 0;
        foreach (var enemy in card.CombatState.Enemies)
        {
            if (enemy.IsAlive)
            {
                living++;
                target = enemy;
            }
        }

        if (living != 1)
        {
            reason = $"living-enemies-{living}";
            return null;
        }

        var node = NCombatRoom.Instance?.GetCreatureNode(target!);
        reason = !target!.IsHittable ? "enemy-unhittable"
            : !card.IsValidTarget(target) ? "invalid-target"
            : !Hook.ShouldAllowTargeting(card.CombatState, target, out _) ? "targeting-hook-blocked"
            : node == null ? "no-creature-node"
            : Eligible;
        return reason == Eligible ? node : null;
    }
}

[HarmonyPatch(typeof(NCardPlay), "ShowMultiCreatureTargetingVisuals")]
internal static class SimpleDragVisualsPatch
{
    [HarmonyPostfix]
    internal static void Postfix(NCardPlay __instance) => SingleEnemyDragPatch.ShowPreview(__instance);
}
