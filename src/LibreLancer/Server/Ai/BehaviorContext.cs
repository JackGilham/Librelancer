using LibreLancer.Server.Components;
using LibreLancer.World;
using LibreLancer.World.Components;

namespace LibreLancer.Server.Ai;

public readonly struct BehaviorContext
{
    public BehaviorContext(
        BehaviorManager manager,
        GameWorld world,
        double time,
        AutopilotComponent autopilot,
        ShipSteeringComponent? steering,
        GameObject? target)
    {
        Manager = manager;
        World = world;
        Time = time;
        Autopilot = autopilot;
        Steering = steering;
        Target = target;
    }

    public BehaviorManager Manager { get; }
    public SNPCComponent Npc => Manager.Npc;
    public GameObject Parent => Npc.Parent;
    public GameWorld World { get; }
    public double Time { get; }
    public AutopilotComponent Autopilot { get; }
    public ShipSteeringComponent? Steering { get; }
    public GameObject? Target { get; }
}
