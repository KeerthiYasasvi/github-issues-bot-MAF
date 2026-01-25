using SupportConcierge.Agents;
using SupportConcierge.Guardrails;
using SupportConcierge.SpecPack;
using SupportConcierge.Tools;
using SupportConcierge.Models;

namespace SupportConcierge.Workflows;

public sealed class WorkflowServices
{
    public ISpecPackLoader SpecPackLoader { get; }
    public StateStoreTool StateStore { get; }
    public IssueFormParser IssueFormParser { get; }
    public CommentComposer CommentComposer { get; }
    public CategoryScorer CategoryScorer { get; }
    public SchemaValidator SchemaValidator { get; }
    public SecretRedactor SecretRedactor { get; private set; }
    public Validators Validators { get; private set; }
    public CompletenessScorer CompletenessScorer { get; private set; }
    public IGitHubTool GitHub { get; }
    public MetricsTool Metrics { get; }

    public ClassifierAgent ClassifierAgent { get; }
    public ExtractorAgent ExtractorAgent { get; }
    public FollowUpAgent FollowUpAgent { get; }
    public BriefAgent BriefAgent { get; }
    public JudgeAgent JudgeAgent { get; }
    public RunContext? LastRunContext { get; set; }

    public WorkflowServices(
        ISpecPackLoader specPackLoader,
        StateStoreTool stateStore,
        IssueFormParser issueFormParser,
        CommentComposer commentComposer,
        CategoryScorer categoryScorer,
        SchemaValidator schemaValidator,
        SecretRedactor secretRedactor,
        Validators validators,
        CompletenessScorer completenessScorer,
        IGitHubTool gitHub,
        MetricsTool metrics,
        ClassifierAgent classifierAgent,
        ExtractorAgent extractorAgent,
        FollowUpAgent followUpAgent,
        BriefAgent briefAgent,
        JudgeAgent judgeAgent)
    {
        SpecPackLoader = specPackLoader;
        StateStore = stateStore;
        IssueFormParser = issueFormParser;
        CommentComposer = commentComposer;
        CategoryScorer = categoryScorer;
        SchemaValidator = schemaValidator;
        SecretRedactor = secretRedactor;
        Validators = validators;
        CompletenessScorer = completenessScorer;
        GitHub = gitHub;
        Metrics = metrics;
        ClassifierAgent = classifierAgent;
        ExtractorAgent = extractorAgent;
        FollowUpAgent = followUpAgent;
        BriefAgent = briefAgent;
        JudgeAgent = judgeAgent;
    }

    public void UpdateForSpecPack(SpecPackConfig specPack)
    {
        Validators = new Validators(specPack.Validators);
        SecretRedactor = new SecretRedactor(specPack.Validators.SecretPatterns);
        CompletenessScorer = new CompletenessScorer(Validators);
    }
}
