using SupportConcierge.Core.SpecPack;

namespace SupportConcierge.Core.Tools;

public interface ISpecPackLoader
{
    Task<SpecPackConfig> LoadAsync(CancellationToken cancellationToken = default);
}
