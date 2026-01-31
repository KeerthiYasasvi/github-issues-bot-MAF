using Microsoft.Agents.AI.Workflows;
using SupportConcierge.Core.Modules.Models;
using SupportConcierge.Core.Modules.Tools;

namespace SupportConcierge.Core.Modules.Workflows.Executors;

public sealed class LoadSpecPackExecutor : Executor<RunContext, RunContext>
{
    private readonly ISpecPackLoader _specPackLoader;

    public LoadSpecPackExecutor(ISpecPackLoader specPackLoader)
        : base("load_spec_pack", ExecutorDefaults.Options, false)
    {
        _specPackLoader = specPackLoader;
    }

    public override async ValueTask<RunContext> HandleAsync(RunContext input, IWorkflowContext context, CancellationToken ct = default)
    {
        try
        {
            input.SpecPack = await _specPackLoader.LoadAsync(ct);
            Console.WriteLine($"[MAF] LoadSpecPack: Loaded {input.SpecPack.Categories.Count} categories");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAF] LoadSpecPack: Failed to load spec pack - {ex.Message}");
        }

        return input;
    }
}

