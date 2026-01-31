using SupportConcierge.Core.Modules.SpecPack;

namespace SupportConcierge.Core.Modules.Tools;

public sealed class FakeSpecPackLoader : ISpecPackLoader
{
    private readonly SpecPackConfig _specPack;

    public FakeSpecPackLoader(SpecPackConfig specPack)
    {
        _specPack = specPack;
    }

    public Task<SpecPackConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_specPack);
    }
}

