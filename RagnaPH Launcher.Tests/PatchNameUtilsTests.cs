using System.IO;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Launcher.Tests;

public class PatchNameUtilsTests
{
    [Theory]
    [InlineData("patch%201.thor", "patch 1.thor", "patch%201.thor")]
    [InlineData("folder%2Epatch.thor", "folder.patch.thor", "folder.patch.thor")]
    public void Normalize_ReturnsDecodedAndEncoded(string input, string decoded, string encoded)
    {
        var (d, e) = PatchNameUtils.Normalize(input);
        Assert.Equal(decoded, d);
        Assert.Equal(encoded, e);
    }

    [Theory]
    [InlineData("../evil.thor")]
    [InlineData("..%2fevil.thor")]
    [InlineData("bad%" )]
    public void Normalize_ThrowsOnInvalidNames(string input)
    {
        Assert.Throws<InvalidDataException>(() => PatchNameUtils.Normalize(input));
    }
}
