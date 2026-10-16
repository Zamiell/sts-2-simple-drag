// Engine-free test doubles; the production patches are compiled into this harness unchanged.
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SimpleDrag;

namespace SimpleDrag
{
    internal static class DragDiagnostics
    {
        internal static List<string> Events { get; } = [];
        internal static void Record(NCardPlay play, string message) => Events.Add(message);
    }
}

namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum CardType { Attack, Skill, Power }
    public enum TargetType { AnyEnemy, AnyAlly, AllEnemies }
    public enum CardPreviewMode { Normal, MultiCreatureTargeting }
    public enum PileType { Hand }
}

namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public bool IsAlive { get; set; } = true;
        public bool IsHittable { get; set; } = true;
    }
}

namespace MegaCrit.Sts2.Core.Hooks
{
    public static class Hook
    {
        public static bool ShouldAllowTargeting(CombatState state, Creature target, out object? preventer)
        {
            preventer = null;
            return state.HookAllows;
        }
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Combat
{
    public enum TargetMode { None, ReleaseMouseToTarget, ClickMouseToTarget, Controller }
    public class CombatState
    {
        public List<Creature> Enemies { get; } = [];
        public bool HookAllows { get; set; } = true;
    }
    public class Pile
    {
        public PileType Type => PileType.Hand;
    }
    public class Card
    {
        public CardType Type { get; set; } = CardType.Attack;
        public TargetType TargetType { get; set; } = TargetType.AnyEnemy;
        public CombatState? CombatState { get; set; } = new();
        public bool ValidTarget { get; set; } = true;
        public Pile? Pile { get; set; } = new();
        public bool IsValidTarget(Creature target) => ValidTarget && target.IsAlive;
    }
    public class CardNode
    {
        public Card? Model { get; set; } = new();
        public Creature? PreviewTarget { get; private set; }
        public CardPreviewMode PreviewMode { get; private set; }
        public void SetPreviewTarget(Creature? target) => PreviewTarget = target;
        public void UpdateVisuals(PileType pile, CardPreviewMode mode) => PreviewMode = mode;
    }
    public class Holder
    {
        public CardNode CardNode { get; } = new();
        public Card? CardModel { get => CardNode.Model; set => CardNode.Model = value; }
    }
    public class NCardPlay
    {
        public Holder Holder { get; } = new();
        public virtual void CancelPlayCard() => HideTargetingVisuals();
        protected void ShowMultiCreatureTargetingVisuals() => SimpleDragVisualsPatch.Postfix(this);
        public void HideTargetingVisuals()
        {
            Holder.CardNode.SetPreviewTarget(null);
            Holder.CardNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            if (NCombatRoom.Instance is { } room)
            {
                foreach (var node in room.Nodes.Values)
                {
                    node.HideMultiselectReticle();
                }
            }
        }
    }
    public class NMouseCardPlay : NCardPlay
    {
        private Creature? _target;
        private bool _skipStartCardDrag;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        public Creature? Target { get => _target; set => _target = value; }
        public bool StartedWithShortcut { get => _skipStartCardDrag; set => _skipStartCardDrag = value; }
        public CancellationTokenSource Cancellation => _cancellationTokenSource;
        public float MouseY { get; set; } = 700;
        public int UntargetedCalls { get; private set; }
        public int OriginalCalls { get; private set; }
        public TaskCompletionSource MouseReleased { get; } = new();
        private bool IsCardInPlayZone() => MouseY < 750;
        private async Task MultiCreatureTargeting(TargetMode mode)
        {
            UntargetedCalls++;
            RefreshVisuals();
            await MouseReleased.Task;
            if (MouseY > 950)
            {
                CancelPlayCard();
            }
        }
        public void RefreshVisuals()
        {
            if (IsCardInPlayZone())
            {
                ShowMultiCreatureTargetingVisuals();
            }
            else
            {
                HideTargetingVisuals();
            }
        }
        private Task SingleCreatureTargeting(TargetMode mode, TargetType type)
        {
            OriginalCalls++;
            return Task.CompletedTask;
        }
        public Task BeginSelection(TargetMode mode, TargetType type)
        {
            Task result = Task.CompletedTask;
            return SingleEnemyDragPatch.Prefix(this, mode, type, ref result)
                ? SingleCreatureTargeting(mode, type)
                : result;
        }
        public override void CancelPlayCard()
        {
            base.CancelPlayCard();
            _cancellationTokenSource.Cancel();
        }
    }
    public class NCreature
    {
        public Creature Entity { get; }
        public bool ReticleVisible { get; private set; }
        public NCreature(Creature entity) => Entity = entity;
        public void ShowMultiselectReticle() => ReticleVisible = true;
        public void HideMultiselectReticle() => ReticleVisible = false;
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Rooms
{
    public class NCombatRoom
    {
        public static NCombatRoom? Instance { get; set; }
        public Dictionary<Creature, NCreature> Nodes { get; } = [];
        public NCreature? GetCreatureNode(Creature creature) => Nodes.GetValueOrDefault(creature);
    }
}
