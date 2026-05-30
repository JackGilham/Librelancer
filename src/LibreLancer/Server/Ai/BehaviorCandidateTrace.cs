namespace LibreLancer.Server.Ai;

public readonly record struct BehaviorCandidateTrace(
    StateGraphEntry Entry,
    float GraphWeight,
    BehaviorEvaluateResult Result,
    float Score,
    bool DirectiveFallback);

public readonly record struct BehaviorChoice(
    StateGraphEntry Entry,
    BehaviorEvaluateResult Result,
    float Score,
    bool Immediate,
    bool DirectiveFallback);
