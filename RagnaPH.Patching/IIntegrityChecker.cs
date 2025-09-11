using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

public interface IIntegrityChecker
{
    Task VerifyAsync(string filePath, IThorReader thor, CancellationToken ct);
}
