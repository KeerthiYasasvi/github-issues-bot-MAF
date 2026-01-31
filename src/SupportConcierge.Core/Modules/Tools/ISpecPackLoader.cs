using SupportConcierge.Core.Modules.SpecPack;

namespace SupportConcierge.Core.Modules.Tools;

public interface ISpecPackLoader
{
    Task<SpecPackConfig> LoadAsync(CancellationToken cancellationToken = default);
}

