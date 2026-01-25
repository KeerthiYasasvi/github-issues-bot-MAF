using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SupportConcierge.Models;

namespace SupportConcierge.Tools;

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
        return new BotState
        {
            Category = category,
            LoopCount = 0,
            AskedFields = new List<string>(),
            LastUpdated = DateTime.UtcNow,
            IssueAuthor = issueAuthor
        };
    }

    public BotState PruneState(BotState state, int maxAskedFieldsHistory = 20)
    {
        if (state.AskedFields.Count > maxAskedFieldsHistory)
        {
            state.AskedFields = state.AskedFields
                .Skip(state.AskedFields.Count - maxAskedFieldsHistory)
                .ToList();
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
