using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Agents;
using SupportConcierge.Core.Models;
using System.Text.RegularExpressions;

namespace SupportConcierge.Core.Workflows.Executors;

/// <summary>
/// MAF Executor: Detect if the incoming comment is off-topic relative to the issue thread
/// </summary>
public sealed class OffTopicCheckExecutor : Executor<RunContext, RunContext>
{
    private readonly OffTopicAgent _offTopicAgent;
    private const decimal ConfidenceThreshold = 0.75m;

    public OffTopicCheckExecutor(OffTopicAgent offTopicAgent)
        : base("off_topic_check", ExecutorDefaults.Options, false)
    {
        _offTopicAgent = offTopicAgent;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        if (input.ShouldStop)
        {
            return input;
        }

        var isIssueComment = input.EventName == "issue_comment";
        var isIssueEvent = input.EventName == "issues";
        if (!isIssueComment && !isIssueEvent)
        {
            return input;
        }

        if (isIssueComment && input.IncomingComment == null)
        {
            return input;
        }

        if (input.IsStopCommand)
        {
            return input;
        }

        if (input.IsDiagnoseCommand && isIssueComment)
        {
            var remaining = StripCommands(input.IncomingComment.Body ?? string.Empty);
            if (string.IsNullOrWhiteSpace(remaining))
            {
                return input;
            }
        }

        Console.WriteLine("[MAF] OffTopic: Evaluating comment relevance to issue thread");
        var assessment = await _offTopicAgent.EvaluateAsync(input, ct);
        input.OffTopicAssessment = assessment;

        var isOffTopic = assessment.OffTopic && assessment.ConfidenceScore >= ConfidenceThreshold;
        if (isOffTopic)
        {
            input.ShouldRedirectOffTopic = true;
            input.DecisionPath["off_topic"] = "true";
            if (input.ExecutionState != null)
            {
                input.ExecutionState.LoopActionTaken = "off_topic_redirect";
            }
            var activeConv = input.ActiveUserConversation;
            if (activeConv != null)
            {
                activeConv.OffTopicStrikeCount += 1;
                if (activeConv.OffTopicStrikeCount >= 2)
                {
                    activeConv.IsOffTopicBlocked = true;
                    activeConv.OffTopicBlockedAt = DateTime.UtcNow;
                }
                Console.WriteLine($"[MAF] OffTopic: {input.ActiveParticipant} strike {activeConv.OffTopicStrikeCount}/2");
            }
            Console.WriteLine($"[MAF] OffTopic: Detected off-topic comment (confidence {assessment.ConfidenceScore:0.00}).");
        }
        else
        {
            input.DecisionPath["off_topic"] = "false";
            Console.WriteLine($"[MAF] OffTopic: Comment is in-scope (confidence {assessment.ConfidenceScore:0.00}).");
        }

        return input;
    }

    private static string StripCommands(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var stripped = Regex.Replace(text, @"(?i)\B/(diagnose|stop)\b", string.Empty);
        return stripped.Trim();
    }
}
