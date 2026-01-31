using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Tools;
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
            IssueAuthor = "alice",
            LastUpdated = DateTime.UtcNow,
            UserConversations = new Dictionary<string, UserConversation>
            {
                ["alice"] = new UserConversation
                {
                    Username = "alice",
                    LoopCount = 2,
                    AskedFields = new List<string> { "error_message", "stack_trace" },
                    FirstInteraction = DateTime.UtcNow,
                    LastInteraction = DateTime.UtcNow
                }
            }
        };

        var comment = stateStore.EmbedState("hello", state);
        var extracted = stateStore.ExtractState(comment);

        Assert.NotNull(extracted);
        Assert.Equal(state.Category, extracted!.Category);
        Assert.Equal(state.IssueAuthor, extracted.IssueAuthor);
        Assert.Single(extracted.UserConversations);
        Assert.True(extracted.UserConversations.ContainsKey("alice"));
        Assert.Equal(2, extracted.UserConversations["alice"].LoopCount);
        Assert.Equal(2, extracted.UserConversations["alice"].AskedFields.Count);
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
            LastUpdated = DateTime.UtcNow,
            UserConversations = new Dictionary<string, UserConversation>
            {
                ["bob"] = new UserConversation
                {
                    Username = "bob",
                    LoopCount = 1,
                    AskedFields = largeFields,
                    FirstInteraction = DateTime.UtcNow,
                    LastInteraction = DateTime.UtcNow
                }
            }
        };

        var comment = stateStore.EmbedState("hello", state);
        Assert.Contains("compressed:", comment);

        var extracted = stateStore.ExtractState(comment);
        Assert.NotNull(extracted);
        Assert.Equal(largeFields.Count, extracted!.UserConversations["bob"].AskedFields.Count);
    }

    [Fact]
    public void PruneState_KeepsMostRecentAskedFields()
    {
        var stateStore = new StateStoreTool();
        var state = new BotState
        {
            IssueAuthor = "alice",
            UserConversations = new Dictionary<string, UserConversation>
            {
                ["alice"] = new UserConversation
                {
                    Username = "alice",
                    AskedFields = Enumerable.Range(0, 30).Select(i => $"field_{i}").ToList()
                }
            }
        };

        var pruned = stateStore.PruneState(state, 20);
        Assert.Equal(20, pruned.UserConversations["alice"].AskedFields.Count);
        Assert.Equal("field_10", pruned.UserConversations["alice"].AskedFields.First());
    }
}

