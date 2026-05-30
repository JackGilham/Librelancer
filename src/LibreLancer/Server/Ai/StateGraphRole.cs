namespace LibreLancer.Server.Ai;

public enum StateGraphRole
{
    Leader,
    Escort
}

public static class StateGraphRoleExtensions
{
    public static string ToStateGraphType(this StateGraphRole role) =>
        role == StateGraphRole.Escort ? "ESCORT" : "LEADER";
}
