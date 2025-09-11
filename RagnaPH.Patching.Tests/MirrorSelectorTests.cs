using System;
using System.Collections.Generic;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class MirrorSelectorTests
{
    private sealed record TestConfig(IReadOnlyList<PatchServer> PatchServers, string PreferredPatchServer) : IPatchConfig
    {
        public string DefaultGrfName => "data.grf";
        public bool InPlace => false;
        public bool CheckIntegrity => true;
        public bool CreateGrf => false;
    }

    [Fact]
    public void RotatesThroughMirrors()
    {
        var servers = new List<PatchServer>
        {
            new("main", new Uri("https://a/"), new Uri("https://a/files/")),
            new("backup", new Uri("https://b/"), new Uri("https://b/files/"))
        };
        var cfg = new TestConfig(servers, "main");
        var selector = new MirrorSelector(cfg, TimeSpan.Zero);
        Assert.Equal("main", selector.Current.Name);
        selector.NextOnFailure();
        Assert.Equal("backup", selector.Current.Name);
        selector.NextOnFailure();
        Assert.Equal("main", selector.Current.Name);
    }
}
