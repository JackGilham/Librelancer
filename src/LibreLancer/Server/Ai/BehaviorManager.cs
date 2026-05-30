using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LibreLancer.Data.Schema.Pilots;
using LibreLancer.Missions;
using LibreLancer.Missions.Directives;
using LibreLancer.Server.Components;
using LibreLancer.World;
using LibreLancer.World.Components;

namespace LibreLancer.Server.Ai;

public class BehaviorManager
{
    private readonly Dictionary<StateGraphEntry, AiBehavior> behaviors;
    private AiBehavior? currentBehavior;
    private MissionDirective[]? directives;
    private int directiveIndex = -1;
    private readonly List<BehaviorCandidateTrace> lastTrace = [];

    public BehaviorManager(SNPCComponent npc, Random? random = null)
    {
        Npc = npc;
        Random = random ?? new Random();
        behaviors = AiBehavior.CreateAll();
    }

    public SNPCComponent Npc { get; }
    public Random Random { get; }
    public StateGraphEntry CurrentBehaviorEntry { get; private set; } = StateGraphEntry.NULL;
    public StateGraphRole Role { get; private set; } = StateGraphRole.Leader;
    public IReadOnlyList<BehaviorCandidateTrace> LastEvaluatorResults => lastTrace;
    public BehaviorChoice? LastChoice { get; private set; }
    public MissionDirective? ActiveDirective =>
        directives != null && directiveIndex >= 0 && directiveIndex < directives.Length
            ? directives[directiveIndex]
            : null;

    public void SetDirectives(MissionDirective[]? newDirectives, GameWorld world)
    {
        StopDirectiveBehavior(world);
        ClearArmedDirectives();
        directives = newDirectives;
        directiveIndex = newDirectives == null ? -1 : 0;
        ArmCurrentDirective(world);
    }

    public void DockWith(GameObject target, GameWorld world)
    {
        StopDirectiveBehavior(world);
        ClearArmedDirectives();
        directives = null;
        directiveIndex = -1;
        if (behaviors.TryGetValue(StateGraphEntry.Dock, out var dock))
        {
            dock.ArmTarget(target);
        }
    }

    public void Update(double time, GameWorld world)
    {
        if (!Npc.Parent.TryGetComponent<AutopilotComponent>(out var autopilot))
        {
            return;
        }

        if (autopilot.CurrentBehavior == AutopilotBehaviors.Undock)
        {
            return;
        }

        RefreshRole(world);
        Npc.UpdateReactionTimers(time);

        var target = Npc.SelectHostileTarget(world);
        Npc.LastShootAt = target;
        Npc.Parent.TryGetComponent<ShipSteeringComponent>(out var steering);
        var context = new BehaviorContext(this, world, time, autopilot, steering, target);

        if (currentBehavior != null)
        {
            var interrupt = Choose(context);
            if (interrupt is { Immediate: true } &&
                interrupt.Value.Entry != currentBehavior.Entry)
            {
                Activate(interrupt.Value, context);
                return;
            }
            else
            {
                if (currentBehavior.Update(context))
                {
                    CompleteCurrentBehavior(context, world);
                }

                if (currentBehavior != null)
                {
                    return;
                }
            }
        }

        var choice = Choose(context);
        if (choice != null)
        {
            Activate(choice.Value, context);
        }
    }

    public float GetStateValue(StateGraphEntry row, StateGraphEntry column, float defaultVal = 0) =>
        BehaviorChooser.GetStateValue(Npc.StateGraph, row, column, defaultVal);

    public void RefreshRole(GameWorld world)
    {
        var newRole = Npc.Parent.Formation != null &&
                      Npc.Parent.Formation.LeadShip != Npc.Parent
            ? StateGraphRole.Escort
            : StateGraphRole.Leader;

        if (newRole == Role && Npc.StateGraph != null &&
            Npc.StateGraph.Description.Type.Equals(Role.ToStateGraphType(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Role = newRole;
        var graphName = Npc.StateGraphName;
        if (string.IsNullOrWhiteSpace(graphName))
        {
            return;
        }

        var desc = new StateGraphDescription(graphName.ToUpperInvariant(), Role.ToStateGraphType());
        if (Npc.Manager.World.Server.GameData.Items.Ini.StateGraphDb.Tables.TryGetValue(desc, out var table))
        {
            Npc.StateGraph = table;
        }
    }

    public string GetDebugInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Behavior: {(currentBehavior == null ? "(none)" : currentBehavior.Entry)}");
        sb.AppendLine($"Behavior Row: {CurrentBehaviorEntry}");
        sb.AppendLine($"Role: {Role}");
        sb.AppendLine($"Directive: {ActiveDirective?.ToString() ?? "null"}");
        sb.AppendLine($"Selected Candidate: {LastChoice?.Entry.ToString() ?? "none"}");
        if (lastTrace.Count > 0)
        {
            sb.AppendLine("Evaluators:");
            foreach (var item in lastTrace.Take(8))
            {
                var fallback = item.DirectiveFallback ? " fallback" : "";
                sb.AppendLine($"  {item.Entry}: {item.Result} graph={item.GraphWeight:0.###} score={item.Score:0.###}{fallback}");
            }
        }

        return sb.ToString();
    }

    private BehaviorChoice? Choose(BehaviorContext context)
    {
        lastTrace.Clear();
        var choice = BehaviorChooser.Choose(
            Npc.StateGraph,
            CurrentBehaviorEntry,
            entry => Evaluate(entry, context),
            Random,
            GetDirectiveFallbackEntries(),
            lastTrace);
        LastChoice = choice;
        return choice;
    }

    private BehaviorEvaluateResult Evaluate(StateGraphEntry entry, BehaviorContext context) =>
        behaviors.TryGetValue(entry, out var behavior)
            ? behavior.Evaluate(context)
            : BehaviorEvaluateResult.Rejected;

    private IEnumerable<StateGraphEntry> GetDirectiveFallbackEntries()
    {
        foreach (var behavior in behaviors.Values)
        {
            if (behavior.CanUseDirectiveFallback)
            {
                yield return behavior.Entry;
            }
        }
    }

    private void Activate(BehaviorChoice choice, BehaviorContext context)
    {
        currentBehavior?.Exit(context);
        if (!behaviors.TryGetValue(choice.Entry, out var behavior))
        {
            currentBehavior = null;
            return;
        }

        CurrentBehaviorEntry = choice.Entry;
        currentBehavior = behavior;
        currentBehavior.Enter(context);
    }

    private void CompleteCurrentBehavior(BehaviorContext context, GameWorld world)
    {
        if (currentBehavior == null)
        {
            return;
        }

        var completed = currentBehavior;
        var completedDirective = completed.Directive;
        completed.Exit(context);
        currentBehavior = null;
        CurrentBehaviorEntry = completed.Entry;

        if (completedDirective != null &&
            ReferenceEquals(completedDirective, ActiveDirective))
        {
            completed.ClearDirective();
            directiveIndex++;
            ArmCurrentDirective(world);
        }
    }

    private void StopDirectiveBehavior(GameWorld world)
    {
        if (currentBehavior == null ||
            !Npc.Parent.TryGetComponent<AutopilotComponent>(out var autopilot))
        {
            return;
        }

        Npc.Parent.TryGetComponent<ShipSteeringComponent>(out var steering);
        var context = new BehaviorContext(this, world, 0, autopilot, steering, null);
        currentBehavior.Exit(context);
        currentBehavior = null;
        autopilot.Cancel();
    }

    private void ClearArmedDirectives()
    {
        foreach (var behavior in behaviors.Values)
        {
            behavior.ClearDirective();
        }
    }

    private void ArmCurrentDirective(GameWorld world)
    {
        while (directives != null && directiveIndex >= 0 && directiveIndex < directives.Length)
        {
            var directive = directives[directiveIndex];
            switch (directive)
            {
                case BreakFormationDirective:
                    if (Npc.Parent.Formation != null)
                    {
                        Npc.Parent.Formation.Remove(Npc.Parent);
                        RefreshRole(world);
                    }

                    directiveIndex++;
                    continue;
                case FollowPlayerDirective or FollowDirective or MakeNewFormationDirective:
                    ArmBehavior(StateGraphEntry.Follow, directive);
                    return;
                case GotoVecDirective or GotoShipDirective or GotoSplineDirective or StayInRangeDirective
                    or StayOutOfRangeDirective:
                    ArmBehavior(StateGraphEntry.Goto, directive);
                    return;
                case DockDirective:
                    ArmBehavior(StateGraphEntry.Dock, directive);
                    return;
                case DelayDirective:
                    ArmBehavior(StateGraphEntry.Delay, directive);
                    return;
                case IdleDirective:
                    ArmBehavior(StateGraphEntry.Idle, directive);
                    return;
                default:
                    directiveIndex++;
                    continue;
            }
        }

        directives = null;
        directiveIndex = -1;
    }

    private void ArmBehavior(StateGraphEntry entry, MissionDirective directive)
    {
        if (behaviors.TryGetValue(entry, out var behavior))
        {
            behavior.Arm(directive);
        }
        else
        {
            directiveIndex++;
        }
    }
}
