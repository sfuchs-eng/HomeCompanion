using SRF.Knx.Config.Domain;

namespace HomeCompanion.Support.Knx;

public interface IHomeCompanionKnxConfigFactory
{
    Task UpdateHomeCompanionCodeFilesAsync(Action<Dictionary<string, KnxValueConfiguration>>? postProcessEntries = null, CancellationToken cancellationToken = default);
}
