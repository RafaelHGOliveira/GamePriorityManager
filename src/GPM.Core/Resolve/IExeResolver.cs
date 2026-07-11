using GPM.Core.Models;

namespace GPM.Core.Resolve;

public sealed record ExeResolution(string? ExePath, bool NeedsConfirmation, IReadOnlyList<string> Candidates);

public interface IExeResolver
{
    ExeResolution Resolve(Game game);
}
