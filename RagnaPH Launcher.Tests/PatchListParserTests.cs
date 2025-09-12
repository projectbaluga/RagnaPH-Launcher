using System.Linq;
using RagnaPH.Launcher.Services;
using Xunit;

namespace RagnaPH.Launcher.Tests;

public class PatchListParserTests
{
    [Fact]
    public void Parse_StripsNumericPrefixAndBuildsRelativePath()
    {
        var text = "001 patch1.0 - item description fix.thor";
        var items = PatchListParser.Parse(text, baseUrlEndsWithData: false).ToList();

        Assert.Single(items);
        var item = items[0];
        Assert.Equal(1, item.Id);
        Assert.Equal("patch1.0 - item description fix.thor", item.FileName);
        Assert.Equal("data/patch1.0 - item description fix.thor", item.RelativePath);
    }

    [Fact]
    public void Parse_RespectsExistingDataSegment()
    {
        var text = "002 another.thor";
        var items = PatchListParser.Parse(text, baseUrlEndsWithData: true).ToList();

        Assert.Single(items);
        var item = items[0];
        Assert.Equal(2, item.Id);
        Assert.Equal("another.thor", item.FileName);
        Assert.Equal("another.thor", item.RelativePath);
    }
}

