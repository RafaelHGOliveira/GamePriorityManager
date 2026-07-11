using GPM.Core.Models;

namespace GPM.Core.Detection;

public interface IGameSource
{
    GameStore Store { get; }
    bool IsBeta { get; } // EA/Ubisoft ship behind the "beta sources" toggle
    Task<IReadOnlyList<Game>> ScanAsync(CancellationToken ct = default);
}
