using System;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Launcher.Tests;

public class PatchListParserPatchingTests
{
    [Fact]
    public void Parse_HandlesMetadataAfterDelimiter()
    {
        var text = "001 patch1.0  item description fix.thor|20240610";
        var plan = PatchListParser.Parse(text, "http://example.com/base/");

        var job = Assert.Single(plan.Jobs);
        Assert.Equal(1, job.Id);
        Assert.Equal("patch1.0  item description fix.thor", job.FileName);
        Assert.Equal(new Uri("http://example.com/base/patch1.0%20%20item%20description%20fix.thor"), job.DownloadUrl);
    }
}

