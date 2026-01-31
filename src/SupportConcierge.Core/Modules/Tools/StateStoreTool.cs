using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SupportConcierge.Core.Modules.Models;

namespace SupportConcierge.Core.Modules.Tools;

public sealed class StateStoreTool
{
    private const string CodeFenceMarkerPrefix = "```supportbot-state\n";
    private const string CodeFenceMarkerSuffix = "\n```";
    private const string HtmlMarkerPrefix = "<!-- supportbot-state\n";
    private const string HtmlMarkerSuffix = "\n-->";
    private const int CompressionThresholdBytes = 2000;

    public BotState? ExtractState(string commentBody)
    {
        if (string.IsNullOrWhiteSpace(commentBody))
        {
            Console.WriteLine("[StateStore] ExtractState: Empty comment body");
            return null;
        }

        var codeFencePattern = Regex.Escape(CodeFenceMarkerPrefix) + @"(.+?)" + Regex.Escape(CodeFenceMarkerSuffix);
        var matches = Regex.Matches(commentBody, codeFencePattern, RegexOptions.Singleline);
        
        Console.WriteLine($"[StateStore] ExtractState: Searching for code fence pattern: supportbot-state");
        Console.WriteLine($"[StateStore] ExtractState: Found {matches.Count} matches");
        
        var data = string.Empty;
        if (matches.Count > 0)
        {
            data = matches[^1].Groups[1].Value.Trim();
        }
        else
        {
            var htmlPattern = @"<!--\s*supportbot-state\s*[:\n]?(?<data>.+?)-->";
            var htmlMatches = Regex.Matches(commentBody, htmlPattern, RegexOptions.Singleline);
            Console.WriteLine($"[StateStore] ExtractState: Searching for HTML comment pattern: supportbot-state");
            Console.WriteLine($"[StateStore] ExtractState: Found {htmlMatches.Count} matches");
            if (htmlMatches.Count > 0)
            {
                data = htmlMatches[^1].Groups["data"].Value.Trim();
            }
            else
            {
                // Check if there's ANY HTML comment in the body
                var anyHtmlComment = Regex.IsMatch(commentBody, @"<!--.+?-->", RegexOptions.Singleline);
                Console.WriteLine($"[StateStore] ExtractState: Contains HTML comments: {anyHtmlComment}");
                
                // Show last 200 chars of comment body to see if state marker is present
                var bodyEnd = commentBody.Length > 200 ? commentBody.Substring(commentBody.Length - 200) : commentBody;
                Console.WriteLine($"[StateStore] ExtractState: End of comment body: {bodyEnd}");
                
                return null;
            }
        }

        try
        {
            Console.WriteLine($"[StateStore] ExtractState: Extracted data length: {data.Length} chars");
            
            if (data.StartsWith("compressed:", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[StateStore] ExtractState: Data is compressed, decompressing...");
                var compressed = data.Substring("compressed:".Length);
                data = DecompressString(compressed);
                Console.WriteLine($"[StateStore] ExtractState: Decompressed data length: {data.Length} chars");
            }

            var state = JsonSerializer.Deserialize<BotState>(data);
            Console.WriteLine($"[StateStore] ExtractState: ✓ Successfully deserialized state - Category={state?.Category}");
            return state;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StateStore] ExtractState: ✗ Failed to deserialize: {ex.Message}");
            return null;
        }
    }

    public string EmbedState(string commentBody, BotState state)
    {
        var json = JsonSerializer.Serialize(state);
        var size = Encoding.UTF8.GetByteCount(json);

        var stateComment = size > CompressionThresholdBytes
            ? $"{HtmlMarkerPrefix}compressed:{CompressString(json)}{HtmlMarkerSuffix}"
            : $"{HtmlMarkerPrefix}{json}{HtmlMarkerSuffix}";

        var cleanedBody = RemoveState(commentBody);
        return $"{cleanedBody}\n\n{stateComment}";
    }

    public string RemoveState(string commentBody)
    {
        if (string.IsNullOrWhiteSpace(commentBody))
        {
            return commentBody;
        }

        var codeFencePattern = Regex.Escape(CodeFenceMarkerPrefix) + @".+?" + Regex.Escape(CodeFenceMarkerSuffix);
        var withoutCodeFence = Regex.Replace(commentBody, codeFencePattern, string.Empty, RegexOptions.Singleline);
        var htmlPattern = @"<!--\s*supportbot-state\s*[:\n]?.+?-->";
        var withoutHtml = Regex.Replace(withoutCodeFence, htmlPattern, string.Empty, RegexOptions.Singleline);
        return withoutHtml.Trim();
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

