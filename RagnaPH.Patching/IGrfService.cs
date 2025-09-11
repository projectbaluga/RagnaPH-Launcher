using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

public interface IGrfService
{
    Task ApplyAsync(string grfPath, IThorReader thor, bool inPlace, bool createIfMissing, CancellationToken ct);
}
