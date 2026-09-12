using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ChatToolCallAccumulatorTests
{
    [Fact]
    public void AccumulatesFragmentedArgumentsForOneCall()
    {
        var accumulator = new ChatToolCallAccumulator();
        accumulator.Apply(0, "call_1", "ssh_run_command", "{\"profi");
        accumulator.Apply(0, null, null, "leId\":\"a\",");
        accumulator.Apply(0, null, null, "\"command\":\"ls\"}");

        var calls = accumulator.Build();

        Assert.Single(calls);
        Assert.Equal("call_1", calls[0].Id);
        Assert.Equal("ssh_run_command", calls[0].FunctionName);
        Assert.Equal("{\"profileId\":\"a\",\"command\":\"ls\"}", calls[0].ArgumentsJson);
    }

    [Fact]
    public void HandlesInterleavedMultiToolCallDeltas()
    {
        var accumulator = new ChatToolCallAccumulator();
        accumulator.Apply(0, "call_a", "ping", "{\"ho");
        accumulator.Apply(1, "call_b", "dns_lookup", "{\"dom");
        accumulator.Apply(0, null, null, "st\":\"x\"}");
        accumulator.Apply(1, null, null, "ain\":\"y\"}");

        var calls = accumulator.Build();

        Assert.Equal(2, calls.Count);
        Assert.Equal("call_a", calls[0].Id);
        Assert.Equal("ping", calls[0].FunctionName);
        Assert.Equal("{\"host\":\"x\"}", calls[0].ArgumentsJson);
        Assert.Equal("call_b", calls[1].Id);
        Assert.Equal("dns_lookup", calls[1].FunctionName);
        Assert.Equal("{\"domain\":\"y\"}", calls[1].ArgumentsJson);
    }

    [Fact]
    public void EmptyAccumulatorHasNoCalls()
    {
        var accumulator = new ChatToolCallAccumulator();

        Assert.False(accumulator.HasAny);
        Assert.Empty(accumulator.Build());
    }

    [Fact]
    public void ResetClearsAccumulatedState()
    {
        var accumulator = new ChatToolCallAccumulator();
        accumulator.Apply(0, "call_1", "ping", "{}");

        accumulator.Reset();

        Assert.False(accumulator.HasAny);
    }
}
