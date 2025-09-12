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

        var plan = PatchListParser.Parse(plist, "http://ragna.ph/patcher/data/");

        Assert.Equal(4, plan.HighestRemoteId);
        Assert.Equal(4, plan.Jobs.Count);
        Assert.Equal(1, plan.Jobs.First().Id);
        Assert.Equal(1048576, plan.Jobs.First().SizeBytes);
        Assert.Equal(3, plan.Jobs[2].Id);
    }
}
