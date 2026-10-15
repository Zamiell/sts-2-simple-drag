using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SimpleDrag;

var routingTests = new (string Name, Action<Fixture> Arrange, bool Simplified)[]
{
    ("single living enemy", _ => { }, true),
    ("dead enemy alongside sole survivor", f => f.AddEnemy(alive: false), true),
    ("two living enemies", f => f.AddEnemy(), false),
    ("second living enemy is unhittable", f => f.AddEnemy(hittable: false), false),
    ("no enemies", f => f.Card.CombatState!.Enemies.Clear(), false),
    ("only enemy is dead", f => f.Enemy.IsAlive = false, false),
    ("only enemy is unhittable", f => f.Enemy.IsHittable = false, false),
    ("invalid card target", f => f.Card.ValidTarget = false, false),
    ("targeting hook rejects enemy", f => f.Card.CombatState!.HookAllows = false, false),
    ("missing creature node", f => f.Room.Nodes.Clear(), false),
    ("missing combat room", _ => NCombatRoom.Instance = null, false),
    ("missing combat state", f => f.Card.CombatState = null, false),
    ("missing card", f => f.Play.Holder.CardModel = null, false),
    ("skill unchanged", f => f.Card.Type = CardType.Skill, false),
    ("ally-target card unchanged", f => f.Card.TargetType = TargetType.AnyAlly, false),
    ("area attack unchanged", f => f.Card.TargetType = TargetType.AllEnemies, false),
    ("ally-target selection unchanged", f => f.TargetType = TargetType.AnyAlly, false),
    ("click targeting unchanged", f => f.Mode = TargetMode.ClickMouseToTarget, false),
    ("controller targeting unchanged", f => f.Mode = TargetMode.Controller, false),
    ("inactive targeting unchanged", f => f.Mode = TargetMode.None, false),
    ("shortcut unchanged", f => f.Play.StartedWithShortcut = true, false),
    ("already cancelled drag unchanged", f => f.Play.CancelPlayCard(), false)
};

foreach (var (name, arrange, simplified) in routingTests)
{
    var f = new Fixture();
    arrange(f);
    var task = f.Begin();
    Assert(f.Play.OriginalCalls == (simplified ? 0 : 1), $"{name}: arrow/centering path");
    Assert(f.Play.UntargetedCalls == (simplified ? 1 : 0), $"{name}: untargeted drag path");
    if (simplified)
    {
        Assert(!task.IsCompleted && f.Play.Target == null, $"{name}: must wait for release");
        Assert(f.Play.Holder.CardNode.PreviewTarget == f.Enemy, $"{name}: damage preview");
        Assert(f.Play.Holder.CardNode.PreviewMode == CardPreviewMode.MultiCreatureTargeting,
            $"{name}: untargeted preview mode");
        Assert(f.Room.GetCreatureNode(f.Enemy)!.ReticleVisible, $"{name}: enemy highlight");
    }
    f.Play.MouseReleased.SetResult();
    await task;
    Assert(f.Play.Target == (simplified ? f.Enemy : null), $"{name}: selected target");
    AssertInactive(f);
}

var releaseTests = new (string Name, Action<Fixture> DuringDrag, bool Plays)[]
{
    ("ordinary release", _ => { }, true),
    ("right-click or shortcut cancellation", f => f.Play.CancelPlayCard(), false),
    ("scene exit cancellation", f => f.Play.Cancellation.Cancel(), false),
    ("on play-zone boundary", f => f.Play.MouseY = 750, false),
    ("below play zone", f => f.Play.MouseY = 800, false),
    ("inside cancel zone", f => f.Play.MouseY = 980, false),
    ("enemy dies during drag", f => f.Enemy.IsAlive = false, false),
    ("enemy becomes unhittable", f => f.Enemy.IsHittable = false, false),
    ("target hook changes", f => f.Card.CombatState!.HookAllows = false, false),
    ("card target becomes invalid", f => f.Card.ValidTarget = false, false),
    ("enemy spawns during drag", f => f.AddEnemy(), false),
    ("combat disappears", f => f.Card.CombatState = null, false),
    ("enemy node disappears", f => f.Room.Nodes.Clear(), false),
    ("card disappears", f => f.Play.Holder.CardModel = null, false)
};

foreach (var (name, duringDrag, plays) in releaseTests)
{
    var f = new Fixture();
    var task = f.Begin();
    duringDrag(f);
    f.Play.MouseReleased.SetResult();
    await task;
    Assert(f.Play.Target == (plays ? f.Enemy : null), $"{name}: release-time revalidation");
    Assert(f.Play.Cancellation.IsCancellationRequested == !plays, $"{name}: cancellation");
    Assert(f.Play.OriginalCalls == 0, $"{name}: no targeting arrow during simplified drag");
    AssertInactive(f);
}

var moving = new Fixture();
var movingTask = moving.Begin();
moving.Play.MouseY = 800;
moving.Play.RefreshVisuals();
Assert(moving.Play.Holder.CardNode.PreviewTarget == null, "preview clears below play zone");
Assert(!moving.Room.GetCreatureNode(moving.Enemy)!.ReticleVisible, "reticle clears below play zone");
moving.Play.MouseY = 700;
moving.Play.RefreshVisuals();
Assert(moving.Play.Holder.CardNode.PreviewTarget == moving.Enemy, "preview restores on re-entry");
moving.Play.MouseReleased.SetResult();
await movingTask;
AssertInactive(moving);

var failure = new Fixture();
var failedTask = failure.Begin();
failure.Play.MouseReleased.SetException(new InvalidOperationException("simulated engine failure"));
try
{
    await failedTask;
    throw new Exception("FAIL: drag failure was swallowed");
}
catch (InvalidOperationException ex) when (ex.Message == "simulated engine failure")
{
    AssertInactive(failure);
}

Console.WriteLine($"PASS: {routingTests.Length} routing cases, {releaseTests.Length} release cases, preview re-entry and failure cleanup.");

static void AssertInactive(Fixture f)
{
    f.Play.HideTargetingVisuals();
    SimpleDragVisualsPatch.Postfix(f.Play);
    Assert(f.Play.Holder.CardNode.PreviewTarget == null, "preview patch must stop after drag completes");
    Assert(!f.Room.Nodes.Values.Any(node => node.ReticleVisible), "reticle cleanup");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {message}");
    }
}

sealed class Fixture
{
    public NMouseCardPlay Play { get; } = new();
    public NCombatRoom Room { get; } = new();
    public Creature Enemy { get; } = new();
    public Card Card => Play.Holder.CardModel!;
    public TargetMode Mode { get; set; } = TargetMode.ReleaseMouseToTarget;
    public TargetType TargetType { get; set; } = TargetType.AnyEnemy;

    public Fixture()
    {
        NCombatRoom.Instance = Room;
        Card.CombatState!.Enemies.Add(Enemy);
        Room.Nodes.Add(Enemy, new NCreature(Enemy));
    }

    public void AddEnemy(bool alive = true, bool hittable = true)
    {
        var enemy = new Creature { IsAlive = alive, IsHittable = hittable };
        Card.CombatState!.Enemies.Add(enemy);
        Room.Nodes.Add(enemy, new NCreature(enemy));
    }

    public Task Begin() => Play.BeginSelection(Mode, TargetType);
}
