using System;
using System.Collections.Generic;
using LibreLancer.Data.Schema.Pilots;
using LibreLancer.Server.Ai;
using Xunit;

namespace LibreLancer.Tests.Server.Ai;

public class BehaviorChooserTests
{
    [Fact]
    public void ZeroGraphValueSkipsEvaluator()
    {
        var graph = MakeGraph();
        var called = false;

        var choice = BehaviorChooser.Choose(
            graph,
            StateGraphEntry.NULL,
            _ =>
            {
                called = true;
                return BehaviorEvaluateResult.Weighted;
            },
            new Random(1));

        Assert.Null(choice);
        Assert.False(called);
    }

    [Fact]
    public void WeightedUsesGraphValue()
    {
        var graph = MakeGraph();
        graph.Data[(int)StateGraphEntry.NULL][(int)StateGraphEntry.Goto] = 7;
        List<BehaviorCandidateTrace> trace = [];

        var choice = BehaviorChooser.Choose(
            graph,
            StateGraphEntry.NULL,
            _ => BehaviorEvaluateResult.Weighted,
            new Random(1),
            trace: trace);

        Assert.NotNull(choice);
        Assert.Equal(StateGraphEntry.Goto, choice.Value.Entry);
        Assert.Equal(7, choice.Value.Score);
        Assert.Single(trace);
        Assert.Equal(7, trace[0].Score);
    }

    [Fact]
    public void ImmediateBeatsHigherWeightedCandidate()
    {
        var graph = MakeGraph();
        graph.Data[(int)StateGraphEntry.NULL][(int)StateGraphEntry.Goto] = 100;
        graph.Data[(int)StateGraphEntry.NULL][(int)StateGraphEntry.Dock] = 1;

        var choice = BehaviorChooser.Choose(
            graph,
            StateGraphEntry.NULL,
            entry => entry == StateGraphEntry.Dock
                ? BehaviorEvaluateResult.Immediate
                : BehaviorEvaluateResult.Weighted,
            new Random(1));

        Assert.NotNull(choice);
        Assert.Equal(StateGraphEntry.Dock, choice.Value.Entry);
        Assert.True(choice.Value.Immediate);
    }

    [Fact]
    public void RejectedAndActiveCannotBeSelected()
    {
        var graph = MakeGraph();
        graph.Data[(int)StateGraphEntry.NULL][(int)StateGraphEntry.Goto] = 1;
        graph.Data[(int)StateGraphEntry.NULL][(int)StateGraphEntry.Dock] = 1;

        var choice = BehaviorChooser.Choose(
            graph,
            StateGraphEntry.NULL,
            entry => entry == StateGraphEntry.Goto
                ? BehaviorEvaluateResult.Rejected
                : BehaviorEvaluateResult.Active,
            new Random(1));

        Assert.Null(choice);
    }

    [Fact]
    public void SeededWeightedSelectionIsDeterministic()
    {
        var graph = MakeGraph();
        graph.Data[(int)StateGraphEntry.NULL][(int)StateGraphEntry.Buzz] = 4;
        graph.Data[(int)StateGraphEntry.NULL][(int)StateGraphEntry.Goto] = 1;
        graph.Data[(int)StateGraphEntry.NULL][(int)StateGraphEntry.Dock] = 5;

        var first = BehaviorChooser.Choose(
            graph,
            StateGraphEntry.NULL,
            _ => BehaviorEvaluateResult.Weighted,
            new Random(42));
        var second = BehaviorChooser.Choose(
            graph,
            StateGraphEntry.NULL,
            _ => BehaviorEvaluateResult.Weighted,
            new Random(42));

        Assert.NotNull(first);
        Assert.Equal(first.Value.Entry, second!.Value.Entry);
    }

    [Fact]
    public void DirectiveArmedBehaviorCanUseFallbackScore()
    {
        var graph = MakeGraph();

        var choice = BehaviorChooser.Choose(
            graph,
            StateGraphEntry.NULL,
            _ => BehaviorEvaluateResult.Weighted,
            new Random(1),
            [StateGraphEntry.Goto]);

        Assert.NotNull(choice);
        Assert.Equal(StateGraphEntry.Goto, choice.Value.Entry);
        Assert.True(choice.Value.DirectiveFallback);
        Assert.Equal(BehaviorChooser.DirectiveFallbackScore, choice.Value.Score);
    }

    private static StateGraph MakeGraph()
    {
        var graph = new StateGraph(new StateGraphDescription("FIGHTER", "LEADER"));
        for (var i = 0; i < (int)StateGraphEntry._Count; i++)
        {
            graph.Data.Add(new float[(int)StateGraphEntry._Count]);
        }

        return graph;
    }
}
