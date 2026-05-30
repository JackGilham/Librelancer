using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Missions;
using LibreLancer.Missions.Directives;
using LibreLancer.Server.Components;
using LibreLancer.World;
using LibreLancer.World.Components;

namespace LibreLancer.Server.Ai;

public abstract class AiBehavior
{
    protected AiBehavior(StateGraphEntry entry)
    {
        Entry = entry;
    }

    public StateGraphEntry Entry { get; }
    public bool Active { get; private set; }
    public MissionDirective? Directive { get; private set; }
    public bool Armed => Directive != null;
    protected double TimeActive { get; private set; }

    public virtual bool CanUseDirectiveFallback => Armed;

    public void Arm(MissionDirective directive)
    {
        Directive = directive;
        OnArmed(directive);
    }

    public virtual void ArmTarget(GameObject target)
    {
    }

    public void ClearDirective()
    {
        Directive = null;
        OnDirectiveCleared();
    }

    public BehaviorEvaluateResult Evaluate(BehaviorContext context)
    {
        if (Active)
        {
            return BehaviorEvaluateResult.Active;
        }

        return EvaluateImpl(context);
    }

    public void Enter(BehaviorContext context)
    {
        Active = true;
        TimeActive = 0;
        OnEnter(context);
    }

    public bool Update(BehaviorContext context)
    {
        TimeActive += context.Time;
        return OnUpdate(context);
    }

    public void Exit(BehaviorContext context)
    {
        OnExit(context);
        Active = false;
        TimeActive = 0;
    }

    protected virtual void OnArmed(MissionDirective directive)
    {
    }

    protected virtual void OnDirectiveCleared()
    {
    }

    protected abstract BehaviorEvaluateResult EvaluateImpl(BehaviorContext context);

    protected virtual void OnEnter(BehaviorContext context)
    {
    }

    protected virtual bool OnUpdate(BehaviorContext context) => true;

    protected virtual void OnExit(BehaviorContext context)
    {
    }

    protected static float Throttle(float inThrottle) =>
        inThrottle <= 0 ? 1 : inThrottle / 100.0f;

    protected static Vector3 RandomUnit(Random random)
    {
        Vector3 v;
        do
        {
            v = new Vector3(
                random.NextSingle() * 2 - 1,
                random.NextSingle() * 2 - 1,
                random.NextSingle() * 2 - 1);
        } while (v.LengthSquared() < 0.001f);

        return Vector3.Normalize(v);
    }

    protected static void ClearSteering(ShipSteeringComponent? steering)
    {
        if (steering == null)
        {
            return;
        }

        steering.InThrottle = 0;
        steering.InPitch = 0;
        steering.InYaw = 0;
        steering.InRoll = 0;
        steering.Cruise = false;
        steering.Thrust = false;
    }

    internal static Dictionary<StateGraphEntry, AiBehavior> CreateAll()
    {
        return new Dictionary<StateGraphEntry, AiBehavior>
        {
            [StateGraphEntry.NULL] = new NullAiBehavior(),
            [StateGraphEntry.Buzz] = new AttackMoveBehavior(StateGraphEntry.Buzz),
            [StateGraphEntry.Goto] = new GotoAiBehavior(),
            [StateGraphEntry.Trail] = new AttackMoveBehavior(StateGraphEntry.Trail),
            [StateGraphEntry.Flee] = new FleeAiBehavior(),
            [StateGraphEntry.Evade] = new EvadeAiBehavior(StateGraphEntry.Evade, false),
            [StateGraphEntry.Idle] = new IdleAiBehavior(),
            [StateGraphEntry.Dock] = new DockAiBehavior(),
            [StateGraphEntry.Launch] = new LaunchAiBehavior(),
            [StateGraphEntry.InstantTradeLane] = new RejectedAiBehavior(StateGraphEntry.InstantTradeLane),
            [StateGraphEntry.Formation] = new FormationAiBehavior(),
            [StateGraphEntry.LargeShipMove] = new AttackMoveBehavior(StateGraphEntry.LargeShipMove),
            [StateGraphEntry.Cruise] = new RejectedAiBehavior(StateGraphEntry.Cruise),
            [StateGraphEntry.Strafe] = new AttackMoveBehavior(StateGraphEntry.Strafe),
            [StateGraphEntry.Guide] = new RejectedAiBehavior(StateGraphEntry.Guide),
            [StateGraphEntry.Face] = new AttackMoveBehavior(StateGraphEntry.Face),
            [StateGraphEntry.Loot] = new RejectedAiBehavior(StateGraphEntry.Loot),
            [StateGraphEntry.Follow] = new FollowAiBehavior(),
            [StateGraphEntry.DrasticEvade] = new EvadeAiBehavior(StateGraphEntry.DrasticEvade, true),
            [StateGraphEntry.FreeFlight] = new RejectedAiBehavior(StateGraphEntry.FreeFlight),
            [StateGraphEntry.Delay] = new DelayAiBehavior()
        };
    }

    private sealed class NullAiBehavior() : AiBehavior(StateGraphEntry.NULL)
    {
        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context) =>
            context.Target == null ? BehaviorEvaluateResult.Weighted : BehaviorEvaluateResult.Rejected;

        protected override void OnEnter(BehaviorContext context)
        {
            context.Autopilot.Cancel();
            ClearSteering(context.Steering);
        }
    }

    private sealed class RejectedAiBehavior(StateGraphEntry entry) : AiBehavior(entry)
    {
        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context) =>
            BehaviorEvaluateResult.Rejected;
    }

    private sealed class LaunchAiBehavior() : AiBehavior(StateGraphEntry.Launch)
    {
        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context) =>
            context.Autopilot.CurrentBehavior == AutopilotBehaviors.Undock
                ? BehaviorEvaluateResult.Active
                : BehaviorEvaluateResult.Rejected;
    }

    private sealed class IdleAiBehavior() : AiBehavior(StateGraphEntry.Idle)
    {
        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context) =>
            Armed ? BehaviorEvaluateResult.Weighted : BehaviorEvaluateResult.Rejected;

        protected override void OnEnter(BehaviorContext context)
        {
            context.Autopilot.Cancel();
            ClearSteering(context.Steering);
            if (Armed && context.Parent.Formation != null)
            {
                context.Parent.Formation.Remove(context.Parent);
                context.Manager.RefreshRole(context.World);
            }
        }
    }

    private sealed class DelayAiBehavior() : AiBehavior(StateGraphEntry.Delay)
    {
        private double remaining;

        protected override void OnArmed(MissionDirective directive)
        {
            remaining = directive is DelayDirective delay ? delay.Time : 0;
        }

        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context) =>
            Directive is DelayDirective ? BehaviorEvaluateResult.Weighted : BehaviorEvaluateResult.Rejected;

        protected override bool OnUpdate(BehaviorContext context)
        {
            remaining -= context.Time;
            return remaining <= 0;
        }
    }

    private sealed class DockAiBehavior() : AiBehavior(StateGraphEntry.Dock)
    {
        private GameObject? directTarget;
        private bool failedStart;

        public override bool CanUseDirectiveFallback => Armed || directTarget != null;

        public override void ArmTarget(GameObject target)
        {
            directTarget = target;
        }

        protected override void OnDirectiveCleared()
        {
            directTarget = null;
        }

        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context) =>
            Directive is DockDirective || directTarget != null
                ? BehaviorEvaluateResult.Weighted
                : BehaviorEvaluateResult.Rejected;

        protected override void OnEnter(BehaviorContext context)
        {
            failedStart = false;
            var target = directTarget;
            if (target == null && Directive is DockDirective dock && !string.IsNullOrWhiteSpace(dock.Target))
            {
                target = context.World.GetObject(dock.Target);
            }

            if (target == null)
            {
                failedStart = true;
                return;
            }

            if (target.TryGetComponent<SDockableComponent>(out var sd))
            {
                sd.StartDock(context.Parent, 0);
            }

            context.Autopilot.StartDock(target, GotoKind.Goto);
        }

        protected override bool OnUpdate(BehaviorContext context) =>
            failedStart || context.Autopilot.CurrentBehavior != AutopilotBehaviors.Dock;
    }

    private sealed class FollowAiBehavior() : AiBehavior(StateGraphEntry.Follow)
    {
        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context) =>
            Directive is FollowDirective or FollowPlayerDirective or MakeNewFormationDirective
                ? BehaviorEvaluateResult.Weighted
                : BehaviorEvaluateResult.Rejected;

        protected override void OnEnter(BehaviorContext context)
        {
            switch (Directive)
            {
                case FollowDirective follow:
                {
                    var target = context.World.GetObject(follow.Target);
                    if (target != null)
                    {
                        FormationTools.EnterFormation(context.Parent, target, follow.Offset);
                    }

                    break;
                }
                case FollowPlayerDirective followPlayer:
                {
                    var player = context.World.GetObject("Player");
                    if (player != null)
                    {
                        FormationTools.MakeNewFormation(player, context.World, followPlayer.Formation,
                            followPlayer.Ships);
                    }

                    break;
                }
                case MakeNewFormationDirective newFormation:
                    FormationTools.MakeNewFormation(context.Parent, context.World, newFormation.Formation,
                        newFormation.Ships);
                    break;
            }

            context.Manager.RefreshRole(context.World);
        }
    }

    private sealed class FormationAiBehavior() : AiBehavior(StateGraphEntry.Formation)
    {
        public override bool CanUseDirectiveFallback => false;

        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context) =>
            context.Parent.Formation != null && context.Parent.Formation.LeadShip != context.Parent
                ? BehaviorEvaluateResult.Weighted
                : BehaviorEvaluateResult.Rejected;

        protected override void OnEnter(BehaviorContext context)
        {
            context.Autopilot.StartFormation();
        }

        protected override bool OnUpdate(BehaviorContext context)
        {
            if (context.Parent.Formation == null ||
                context.Parent.Formation.LeadShip == context.Parent)
            {
                context.Manager.RefreshRole(context.World);
                return true;
            }

            if (context.Npc.ShouldBreakFormation())
            {
                context.Parent.Formation.Remove(context.Parent);
                context.Manager.RefreshRole(context.World);
                return true;
            }

            if (context.Autopilot.CurrentBehavior != AutopilotBehaviors.Formation)
            {
                context.Autopilot.StartFormation();
            }

            return false;
        }
    }

    private sealed class GotoAiBehavior() : AiBehavior(StateGraphEntry.Goto)
    {
        private int splineIndex = -1;
        private bool failedStart;
        private static readonly float[] SplineTimes = [0, 0.3333f, 0.6667f, 1f];

        protected override void OnArmed(MissionDirective directive)
        {
            splineIndex = -1;
            failedStart = false;
        }

        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context) =>
            Directive is GotoVecDirective or GotoShipDirective or GotoSplineDirective or StayInRangeDirective
                or StayOutOfRangeDirective
                ? BehaviorEvaluateResult.Weighted
                : BehaviorEvaluateResult.Rejected;

        protected override void OnEnter(BehaviorContext context)
        {
            failedStart = false;
            switch (Directive)
            {
                case GotoVecDirective vec:
                    context.Autopilot.GotoVec(vec.Target, vec.CruiseKind, Throttle(vec.MaxThrottle), vec.Range);
                    break;
                case GotoShipDirective ship:
                {
                    var target = context.World.GetObject(ship.Target);
                    if (target == null)
                    {
                        failedStart = true;
                        return;
                    }

                    context.Autopilot.GotoObject(target, ship.CruiseKind, Throttle(ship.MaxThrottle), ship.Range);
                    break;
                }
                case GotoSplineDirective spline:
                    splineIndex = 0;
                    context.Autopilot.GotoVec(EvalSpline(SplineTimes[splineIndex], spline), spline.CruiseKind,
                        Throttle(spline.MaxThrottle), spline.Range);
                    break;
                case StayInRangeDirective stayIn:
                    StartStayIn(context, stayIn);
                    break;
                case StayOutOfRangeDirective stayOut:
                    StartStayOut(context, stayOut);
                    break;
                default:
                    failedStart = true;
                    break;
            }
        }

        protected override bool OnUpdate(BehaviorContext context)
        {
            if (failedStart)
            {
                return true;
            }

            if (Directive is GotoSplineDirective spline &&
                context.Autopilot.CurrentBehavior == AutopilotBehaviors.None)
            {
                if (splineIndex + 1 < SplineTimes.Length)
                {
                    splineIndex++;
                    context.Autopilot.GotoVec(EvalSpline(SplineTimes[splineIndex], spline), spline.CruiseKind,
                        Throttle(spline.MaxThrottle), spline.Range);
                    return false;
                }

                return true;
            }

            return context.Autopilot.CurrentBehavior == AutopilotBehaviors.None;
        }

        private void StartStayIn(BehaviorContext context, StayInRangeDirective stayIn)
        {
            var target = ResolveRangeTarget(context, stayIn.UseObject, stayIn.Object, stayIn.Point, out var point);
            var myPos = context.Parent.WorldTransform.Position;
            if (Vector3.Distance(myPos, point) <= stayIn.Range)
            {
                failedStart = true;
                return;
            }

            if (target != null)
            {
                context.Autopilot.GotoObject(target, GotoKind.Goto, 1, stayIn.Range);
            }
            else
            {
                context.Autopilot.GotoVec(point, GotoKind.Goto, 1, stayIn.Range);
            }
        }

        private void StartStayOut(BehaviorContext context, StayOutOfRangeDirective stayOut)
        {
            ResolveRangeTarget(context, stayOut.UseObject, stayOut.Object, stayOut.Point, out var point);
            var myPos = context.Parent.WorldTransform.Position;
            if (Vector3.Distance(myPos, point) >= stayOut.Range)
            {
                failedStart = true;
                return;
            }

            var away = myPos - point;
            if (away.LengthSquared() < 0.001f)
            {
                away = RandomUnit(context.Manager.Random);
            }
            else
            {
                away = Vector3.Normalize(away);
            }

            context.Autopilot.GotoVec(point + away * (stayOut.Range + 200), GotoKind.Goto, 1, 40);
        }

        private static GameObject? ResolveRangeTarget(BehaviorContext context, bool useObject, string? objectName,
            Vector3 point, out Vector3 resolvedPoint)
        {
            if (useObject && !string.IsNullOrWhiteSpace(objectName))
            {
                var target = context.World.GetObject(objectName);
                if (target != null)
                {
                    resolvedPoint = target.WorldTransform.Position;
                    return target;
                }
            }

            resolvedPoint = point;
            return null;
        }

        private static Vector3 EvalSpline(float t, GotoSplineDirective spline)
        {
            return CatmullRom(spline.PointA, spline.PointB, spline.PointC, spline.PointD, t);
        }

        private static float GetT(float t, float alpha, Vector3 p0, Vector3 p1)
        {
            var d = p1 - p0;
            var a = Vector3.Dot(d, d);
            var b = MathF.Pow(a, alpha * 0.5f);
            return b + t;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t,
            float alpha = 0.5f)
        {
            var t0 = 0.0f;
            var t1 = GetT(t0, alpha, p0, p1);
            var t2 = GetT(t1, alpha, p1, p2);
            var t3 = GetT(t2, alpha, p2, p3);
            t = MathHelper.Lerp(t1, t2, t);
            var a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
            var a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
            var a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
            var b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            var b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }
    }

    private sealed class AttackMoveBehavior : AiBehavior
    {
        private Vector3 buzzDirection;

        public AttackMoveBehavior(StateGraphEntry entry) : base(entry)
        {
        }

        public override bool CanUseDirectiveFallback => false;

        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context)
        {
            if (context.Target == null ||
                context.Parent.TryGetComponent<STradelaneMoveComponent>(out _) ||
                context.Target.TryGetComponent<STradelaneMoveComponent>(out _))
            {
                return BehaviorEvaluateResult.Rejected;
            }

            return BehaviorEvaluateResult.Weighted;
        }

        protected override void OnEnter(BehaviorContext context)
        {
            if (Entry == StateGraphEntry.Buzz)
            {
                buzzDirection = RandomUnit(context.Manager.Random);
            }
        }

        protected override bool OnUpdate(BehaviorContext context)
        {
            var target = context.Target;
            if (target == null)
            {
                return true;
            }

            switch (Entry)
            {
                case StateGraphEntry.Buzz:
                    UpdateBuzz(context, target);
                    break;
                case StateGraphEntry.Strafe:
                    UpdateStrafe(context, target);
                    break;
                case StateGraphEntry.Trail:
                case StateGraphEntry.Face:
                case StateGraphEntry.LargeShipMove:
                    var distance = context.Npc.Pilot?.Trail?.Distance ?? 150;
                    context.Autopilot.GotoObject(target, GotoKind.GotoNoCruise, 1, distance);
                    break;
            }

            context.Npc.FireAtTarget(target, context.Time, context.World);

            return Entry switch
            {
                StateGraphEntry.Buzz => TimeActive >= (context.Npc.Pilot?.BuzzPassBy?.PassByTime ?? 5),
                StateGraphEntry.Trail => TimeActive >= (context.Npc.Pilot?.Trail?.BreakTime ?? 5),
                StateGraphEntry.Face => TimeActive >= 5,
                StateGraphEntry.Strafe => TimeActive >= 5,
                StateGraphEntry.LargeShipMove => TimeActive >= 5,
                _ => true
            };
        }

        private void UpdateBuzz(BehaviorContext context, GameObject target)
        {
            var distance = context.Npc.Pilot?.BuzzPassBy?.DistanceToPassBy ?? 100;
            var destination = target.WorldTransform.Transform(buzzDirection * distance);
            context.Autopilot.GotoVec(destination, GotoKind.GotoNoCruise, 1, 0);
        }

        private void UpdateStrafe(BehaviorContext context, GameObject target)
        {
            var myPos = context.Parent.WorldTransform.Position;
            var away = myPos - target.WorldTransform.Position;
            if (away.LengthSquared() < 0.001f)
            {
                away = RandomUnit(context.Manager.Random);
            }
            else
            {
                away = Vector3.Normalize(away);
            }

            var distance = context.Npc.Pilot?.Strafe?.RunAwayDistance ?? 500;
            var throttle = context.Npc.Pilot?.Strafe?.AttackThrottle ?? 1;
            context.Autopilot.GotoVec(myPos + away * distance, GotoKind.GotoNoCruise, throttle, 40);
        }
    }

    private sealed class EvadeAiBehavior : AiBehavior
    {
        private readonly bool drastic;
        private float evadeX;
        private float evadeY;
        private float evadeZ;
        private bool evadeThrust;

        public EvadeAiBehavior(StateGraphEntry entry, bool drastic) : base(entry)
        {
            this.drastic = drastic;
        }

        public override bool CanUseDirectiveFallback => false;

        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context)
        {
            if (drastic)
            {
                return context.Npc.ShouldDrasticEvade()
                    ? BehaviorEvaluateResult.Immediate
                    : BehaviorEvaluateResult.Rejected;
            }

            if (context.Npc.ShouldEvadeImmediately())
            {
                return BehaviorEvaluateResult.Immediate;
            }

            if (context.Target != null &&
                context.Npc.Pilot?.EvadeDodge != null)
            {
                return BehaviorEvaluateResult.Weighted;
            }

            return BehaviorEvaluateResult.Rejected;
        }

        protected override void OnEnter(BehaviorContext context)
        {
            context.Autopilot.Cancel();
            var turnThrottle = context.Npc.Pilot?.EvadeBreak?.TurnThrottle ?? 1;
            var rollThrottle = context.Npc.Pilot?.EvadeBreak?.RollThrottle ?? 1;
            if (drastic)
            {
                turnThrottle = MathF.Max(turnThrottle, 1);
                rollThrottle = MathF.Max(rollThrottle, 1);
            }

            evadeX = turnThrottle * context.Manager.Random.Next(-1, 2);
            evadeY = turnThrottle * context.Manager.Random.Next(-1, 2);
            evadeZ = rollThrottle * context.Manager.Random.Next(-1, 2);
            evadeThrust = drastic || context.Manager.Random.Next(0, 2) == 1;
        }

        protected override bool OnUpdate(BehaviorContext context)
        {
            context.Autopilot.Cancel();
            if (context.Steering != null)
            {
                context.Steering.InThrottle = 1;
                context.Steering.Cruise = false;
                context.Steering.Thrust = evadeThrust;
                context.Steering.InPitch = evadeX;
                context.Steering.InYaw = evadeY;
                context.Steering.InRoll = evadeZ;
            }

            var duration = drastic
                ? MathF.Max(context.Npc.Pilot?.EvadeBreak?.Time ?? 5, 5)
                : context.Npc.Pilot?.EvadeBreak?.Time ?? context.Npc.Pilot?.EvadeDodge?.DodgeTime ?? 5;
            return TimeActive >= duration;
        }

        protected override void OnExit(BehaviorContext context)
        {
            ClearSteering(context.Steering);
        }
    }

    private sealed class FleeAiBehavior() : AiBehavior(StateGraphEntry.Flee)
    {
        private bool failedStart;

        public override bool CanUseDirectiveFallback => false;

        protected override BehaviorEvaluateResult EvaluateImpl(BehaviorContext context)
        {
            if (ShouldFleeForHull(context) ||
                ShouldFleeNoWeapons(context))
            {
                return BehaviorEvaluateResult.Immediate;
            }

            return context.Npc.HasRecentDamage && context.Target != null
                ? BehaviorEvaluateResult.Weighted
                : BehaviorEvaluateResult.Rejected;
        }

        protected override void OnEnter(BehaviorContext context)
        {
            failedStart = false;
            var dock = FindFriendlyDock(context);
            if (dock != null)
            {
                context.Autopilot.GotoObject(dock, GotoKind.Goto, 1, 250);
                return;
            }

            var myPos = context.Parent.WorldTransform.Position;
            var threat = context.Target?.WorldTransform.Position ?? myPos + Vector3.UnitZ;
            var away = myPos - threat;
            if (away.LengthSquared() < 0.001f)
            {
                away = RandomUnit(context.Manager.Random);
            }
            else
            {
                away = Vector3.Normalize(away);
            }

            context.Autopilot.GotoVec(myPos + away * 5000, GotoKind.Goto, 1, 40);
        }

        protected override bool OnUpdate(BehaviorContext context) =>
            failedStart || context.Autopilot.CurrentBehavior == AutopilotBehaviors.None || TimeActive > 20;

        private static bool ShouldFleeForHull(BehaviorContext context)
        {
            var threshold = context.Npc.Pilot?.Job?.FleeWhenHullDamagedPercent ?? 0;
            if (threshold <= 0)
            {
                return false;
            }

            if (!context.Parent.TryGetComponent<SHealthComponent>(out var health) ||
                health.MaxHealth <= 0)
            {
                return false;
            }

            return (health.CurrentHealth / health.MaxHealth) * 100 <= threshold;
        }

        private static bool ShouldFleeNoWeapons(BehaviorContext context)
        {
            if (!(context.Npc.Pilot?.Job?.FleeNoWeaponsStyle ?? false) ||
                context.Target == null)
            {
                return false;
            }

            foreach (var _ in context.Parent.GetChildComponents<GunComponent>())
            {
                return false;
            }

            foreach (var _ in context.Parent.GetChildComponents<MissileLauncherComponent>())
            {
                return false;
            }

            return true;
        }

        private static GameObject? FindFriendlyDock(BehaviorContext context)
        {
            var myPos = context.Parent.WorldTransform.Position;
            GameObject? selected = null;
            var selectedDistance = float.MaxValue;
            foreach (var other in context.World.SpatialLookup.GetNearbyObjects(context.Parent, myPos, 10000))
            {
                if (other == context.Parent ||
                    !other.TryGetComponent<SDockableComponent>(out _) ||
                    context.Npc.IsHostileTo(other))
                {
                    continue;
                }

                var distance = Vector3.DistanceSquared(myPos, other.WorldTransform.Position);
                if (distance < selectedDistance)
                {
                    selectedDistance = distance;
                    selected = other;
                }
            }

            return selected;
        }
    }
}
