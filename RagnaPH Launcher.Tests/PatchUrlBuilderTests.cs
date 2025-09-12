using System;
using RagnaPH.Launcher.Net;
using Xunit;

namespace RagnaPH.Launcher.Tests;

public class PatchUrlBuilderTests {
    [Fact]
    public void Build_EncodesSpacesAndSpecialCharacters() {
        var baseUri = new Uri("http://ragna.ph/patcher/data");
        var path = "001 patch1.0 - item description fix.thor";
        var uri = PatchUrlBuilder.Build(baseUri, path);
        Assert.Equal("http://ragna.ph/patcher/data/001%20patch1.0%20-%20item%20description%20fix.thor", uri.AbsoluteUri);
    }
}
