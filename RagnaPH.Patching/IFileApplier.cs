using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

public interface IFileApplier
{
    Task ApplyAsync(string gameRoot, IThorReader thor, CancellationToken ct);
}
