using GPM.Core.Models;

namespace GPM.Core.Detection;

public sealed class GameScanner(IEnumerable<IGameSource> sources)
{
    public async Task<IReadOnlyList<Game>> ScanAsync(bool includeBeta, CancellationToken ct = default)
    {
        var tasks = sources
            .Where(s => includeBeta || !s.IsBeta)
            .Select(async s =>
            {
                try { return await s.ScanAsync(ct); }
                catch { return (IReadOnlyList<Game>)[]; } // a failing source never breaks the scan
            });
        return (await Task.WhenAll(tasks)).SelectMany(g => g).ToList();
    }
}
