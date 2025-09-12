using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Launcher.Tests;

public class PatchListParserTests
{
    [Fact]
    public void Parse_NormalizesFilenames()
    {
        var plan = PatchListParser.Parse("1 patch%201.thor", "http://example.com/patch/");
        Assert.Single(plan.Jobs);
        var job = plan.Jobs[0];
        Assert.Equal("patch 1.thor", job.FileName);
        Assert.Equal("http://example.com/patch/patch%201.thor", job.DownloadUrl.ToString());
    }
}
