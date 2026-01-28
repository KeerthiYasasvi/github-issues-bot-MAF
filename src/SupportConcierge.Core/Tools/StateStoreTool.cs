using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SupportConcierge.Core.Models;

namespace SupportConcierge.Core.Tools;

public sealed class StateStoreTool
{
    private const string StateMarkerPrefix = "<!-- supportbot_state:";
    private const string StateMarkerSuffix = " -->";
    private const int CompressionThresholdBytes = 2000;

    public BotState? ExtractState(string commentBody)
    {
        if (string.IsNullOrWhiteSpace(commentBody))
        {
            return null;
        }

        var pattern = Regex.Escape(StateMarkerPrefix) + @"(.+?)" + Regex.Escape(StateMarkerSuffix);
        var matches = Regex.Matches(commentBody, pattern, RegexOptions.Singleline);
        if (matches.Count == 0)
        {
            return null;
        }

        try
        {
            var data = matches[^1].Groups[1].Value.Trim();
            if (data.StartsWith("compressed:", StringComparison.OrdinalIgnoreCase))
            {
                var compressed = data.Substring("compressed:".Length);
                data = DecompressString(compressed);
            }

            return JsonSerializer.Deserialize<BotState>(data);
        }
        catch
        {
            return null;
        }
    }

    public string EmbedState(string commentBody, BotState state)
    {
        var json = JsonSerializer.Serialize(state);
        var size = Encoding.UTF8.GetByteCount(json);

        var stateComment = size > CompressionThresholdBytes
            ? $"{StateMarkerPrefix}compressed:{CompressString(json)}{StateMarkerSuffix}"
            : $"{StateMarkerPrefix}{json}{StateMarkerSuffix}";

        var cleanedBody = RemoveState(commentBody);
        return $"{cleanedBody}\n\n{stateComment}";
    }

    public string RemoveState(string commentBody)
    {
        if (string.IsNullOrWhiteSpace(commentBody))
        {
            return commentBody;
        }

        var pattern = Regex.Escape(StateMarkerPrefix) + @".+?" + Regex.Escape(StateMarkerSuffix);
        return Regex.Replace(commentBody, pattern, string.Empty, RegexOptions.Singleline).Trim();
    }

    public BotState CreateInitialState(string category, string issueAuthor)
    {
        var state = new BotState
        {
            Category = category,
            LastUpdated = DateTime.UtcNow,
            IssueAuthor = issueAuthor
        };

        // Create initial conversation for issue author
        var authorConv = new UserConversation
        {
            Username = issueAuthor,
            LoopCount = 0,
            IsExhausted = false,
            FirstInteraction = DateTime.UtcNow,
            LastInteraction = DateTime.UtcNow
        };

        state.UserConversations[issueAuthor] = authorConv;
        return state;
    }

    public BotState PruneState(BotState state, int maxAskedFieldsHistory = 20)
    {
        // Prune each user conversation's asked fields
        foreach (var userConv in state.UserConversations.Values)
        {
            if (userConv.AskedFields.Count > maxAskedFieldsHistory)
            {
                userConv.AskedFields = userConv.AskedFields
                    .Skip(userConv.AskedFields.Count - maxAskedFieldsHistory)
                    .ToList();
            }
        }

        return state;
    }

    private static string CompressString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            input.CopyTo(gzip);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    private static string DecompressString(string compressedText)
    {
        var bytes = Convert.FromBase64String(compressedText);
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        {
            gzip.CopyTo(output);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }
}
