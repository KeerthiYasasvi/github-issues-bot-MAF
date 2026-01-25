using SupportConcierge.Core.Models;
using SupportConcierge.Core.Tools;
using Xunit;

namespace SupportConcierge.Tests;

public class StateStoreTests
{
    [Fact]
    public void EmbedAndExtractState_RoundTrips()
    {
        var stateStore = new StateStoreTool();
        var state = new BotState
        {
            Category = "runtime",
            LoopCount = 2,
            IssueAuthor = "alice",
            AskedFields = new List<string> { "error_message", "stack_trace" },
            LastUpdated = DateTime.UtcNow
        };

        var comment = stateStore.EmbedState("hello", state);
        var extracted = stateStore.ExtractState(comment);

        Assert.NotNull(extracted);
        Assert.Equal(state.Category, extracted!.Category);
        Assert.Equal(state.LoopCount, extracted.LoopCount);
        Assert.Equal(state.IssueAuthor, extracted.IssueAuthor);
        Assert.Equal(state.AskedFields.Count, extracted.AskedFields.Count);
    }

    [Fact]
    public void EmbedState_CompressesLargePayload()
    {
        var stateStore = new StateStoreTool();
        var largeFields = Enumerable.Range(0, 200).Select(i => $"field_{i}").ToList();
        var state = new BotState
        {
            Category = "build",
            IssueAuthor = "bob",
            AskedFields = largeFields,
            LastUpdated = DateTime.UtcNow
        };

        var comment = stateStore.EmbedState("hello", state);
        Assert.Contains("compressed:", comment);

        var extracted = stateStore.ExtractState(comment);
        Assert.NotNull(extracted);
        Assert.Equal(largeFields.Count, extracted!.AskedFields.Count);
    }

    [Fact]
    public void PruneState_KeepsMostRecentAskedFields()
    {
        var stateStore = new StateStoreTool();
        var state = new BotState
        {
            AskedFields = Enumerable.Range(0, 30).Select(i => $"field_{i}").ToList()
        };

        var pruned = stateStore.PruneState(state, 20);
        Assert.Equal(20, pruned.AskedFields.Count);
        Assert.Equal("field_10", pruned.AskedFields.First());
    }
}
