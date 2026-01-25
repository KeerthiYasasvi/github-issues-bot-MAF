using SupportConcierge.SpecPack;

namespace SupportConcierge.Tools;

public interface ISpecPackLoader
{
    Task<SpecPackConfig> LoadAsync(CancellationToken cancellationToken = default);
}
