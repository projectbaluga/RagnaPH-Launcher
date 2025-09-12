using System.Linq;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class PatchListParserTests
{
    [Fact]
    public void ParsesSamplePlist()
    {
        const string plist = @"# Simple
001|patch_0001.thor|size:1048576|target:data.grf
002|patch_0002.thor
patch_0003.thor
004|patch_0004.thor|sha256:3e7f|target:data.grf";

        var plan = PatchListParser.Parse(plist, "http://ragna.ph/patcher/");

        Assert.Equal(4, plan.HighestRemoteId);
        Assert.Equal(4, plan.Jobs.Count);
        Assert.Equal(1, plan.Jobs.First().Id);
        Assert.Equal(1048576, plan.Jobs.First().SizeBytes);
        Assert.Equal(3, plan.Jobs[2].Id);
    }

    [Fact]
    public void ParsesWhitespaceSeparatedIdAndFileName()
    {
        const string plist = "001\t patch1.0 - item description fix.thor";

        var plan = PatchListParser.Parse(plist, "http://example/patcher/");

        Assert.Single(plan.Jobs);
        Assert.Equal(1, plan.Jobs[0].Id);
        Assert.Equal("patch1.0 - item description fix.thor", plan.Jobs[0].FileName);
        Assert.Equal(new Uri("http://example/patcher/patch1.0%20-%20item%20description%20fix.thor"), plan.Jobs[0].DownloadUrl);
    }
}
