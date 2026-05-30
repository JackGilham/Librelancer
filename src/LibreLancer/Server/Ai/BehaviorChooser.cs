using System;
using System.Collections.Generic;
using LibreLancer.Data.Schema.Pilots;

namespace LibreLancer.Server.Ai;

public static class BehaviorChooser
{
    public const float DirectiveFallbackScore = 0.001f;

    public static float GetStateValue(StateGraph? graph, StateGraphEntry row, StateGraphEntry column,
        float defaultVal = 0)
    {
        if (graph == null)
        {
            return defaultVal;
        }

        if ((int)row >= graph.Data.Count)
        {
            return defaultVal;
        }

        var tableRow = graph.Data[(int)row];

        if ((int)column >= tableRow.Length)
        {
            return defaultVal;
        }

        return tableRow[(int)column];
    }

    public static BehaviorChoice? Choose(
        StateGraph? graph,
        StateGraphEntry current,
        Func<StateGraphEntry, BehaviorEvaluateResult> evaluate,
        Random random,
        IEnumerable<StateGraphEntry>? directiveFallbackEntries = null,
        List<BehaviorCandidateTrace>? trace = null)
    {
        float total = 0;
        List<BehaviorChoice> weighted = [];
        HashSet<StateGraphEntry>? fallbackSet = null;

        for (var i = 0; i < (int)StateGraphEntry._Count; i++)
        {
            var entry = (StateGraphEntry)i;
            var score = GetStateValue(graph, current, entry);

            if (score <= 0)
            {
                continue;
            }

            var result = evaluate(entry);
            if (result == BehaviorEvaluateResult.Immediate)
            {
                trace?.Add(new BehaviorCandidateTrace(entry, score, result, score, false));
                // Multiple immediate candidates are not confirmed from vanilla traces; behavior ID order
                // keeps this deterministic until we recover the tie-break.
                return new BehaviorChoice(entry, result, score, true, false);
            }

            if (result == BehaviorEvaluateResult.Weighted)
            {
                total += score;
                weighted.Add(new BehaviorChoice(entry, result, score, false, false));
                trace?.Add(new BehaviorCandidateTrace(entry, score, result, score, false));
            }
            else
            {
                trace?.Add(new BehaviorCandidateTrace(entry, score, result, 0, false));
            }
        }

        if (directiveFallbackEntries != null)
        {
            fallbackSet = [];
            foreach (var entry in directiveFallbackEntries)
            {
                if (!fallbackSet.Add(entry) ||
                    GetStateValue(graph, current, entry) > 0)
                {
                    continue;
                }

                var result = evaluate(entry);
                if (result == BehaviorEvaluateResult.Immediate)
                {
                    trace?.Add(new BehaviorCandidateTrace(entry, 0, result, DirectiveFallbackScore, true));
                    return new BehaviorChoice(entry, result, DirectiveFallbackScore, true, true);
                }

                if (result == BehaviorEvaluateResult.Weighted)
                {
                    total += DirectiveFallbackScore;
                    weighted.Add(new BehaviorChoice(entry, result, DirectiveFallbackScore, false, true));
                    trace?.Add(new BehaviorCandidateTrace(entry, 0, result, DirectiveFallbackScore, true));
                }
                else
                {
                    trace?.Add(new BehaviorCandidateTrace(entry, 0, result, 0, true));
                }
            }
        }

        if (weighted.Count == 0)
        {
            return null;
        }

        var roll = random.NextSingle() * total;
        for (var i = 0; i < weighted.Count; i++)
        {
            roll -= weighted[i].Score;
            if (roll <= 0)
            {
                return weighted[i];
            }
        }

        return weighted[^1];
    }
}
