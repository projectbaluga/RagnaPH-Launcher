using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class PatchListParserTests
{
    [Fact]
    public void ParsesVariousFormats()
    {
        var content = "001|patch1.thor|size:10\npatch_0002.thor\n#comment\n003|patch3.thor|sha256:abc";
        var plan = PatchListParser.Parse(content, "http://example.com/");
        Assert.Equal(3, plan.HighestRemoteId);
        Assert.Equal(3, plan.Jobs.Count);
        Assert.Equal(1, plan.Jobs[0].Id);
        Assert.Equal("patch1.thor", plan.Jobs[0].FileName);
        Assert.Equal(10, plan.Jobs[0].SizeBytes);
    }
}
