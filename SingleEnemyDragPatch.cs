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
        if (StartedWithShortcut(__instance) || targetType != TargetType.AnyEnemy ||
            Cancellation(__instance).IsCancellationRequested)
        {
            return true;
        }

        // The game picks click targeting when the button is already up once the card reaches the play zone.
        // A fast flick can release above the line before that frame's check, so treat it as a drag release.
        if (targetMode == TargetMode.ClickMouseToTarget && ReleasedInPlayZone.TryGetValue(__instance, out _))
        {
            targetMode = TargetMode.ReleaseMouseToTarget;
        }
        if (targetMode != TargetMode.ReleaseMouseToTarget || GetOnlyTarget(__instance) == null)
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
                return;
            }

            if (!IsCardInPlayZone(play) || GetOnlyTarget(play) is not { } target)
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
            GetOnlyTarget(play) is not { } target)
        {
            return;
        }

        cardNode.SetPreviewTarget(target.Entity);
        cardNode.UpdateVisuals((cardNode.Model?.Pile?.Type).GetValueOrDefault(), CardPreviewMode.MultiCreatureTargeting);
        target.ShowMultiselectReticle();
    }

    private static NCreature? GetOnlyTarget(NCardPlay play)
    {
        var card = play.Holder.CardModel;
        if (card?.CombatState == null || card.Type is not (CardType.Attack or CardType.Skill) ||
            card.TargetType != TargetType.AnyEnemy)
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
                return null;
            }
            target = enemy;
        }

        if (target == null || !target.IsHittable || !card.IsValidTarget(target) ||
            !Hook.ShouldAllowTargeting(card.CombatState, target, out _))
        {
            return null;
        }
        return NCombatRoom.Instance?.GetCreatureNode(target);
    }
}

[HarmonyPatch(typeof(NCardPlay), "ShowMultiCreatureTargetingVisuals")]
internal static class SimpleDragVisualsPatch
{
    [HarmonyPostfix]
    internal static void Postfix(NCardPlay __instance) => SingleEnemyDragPatch.ShowPreview(__instance);
}
