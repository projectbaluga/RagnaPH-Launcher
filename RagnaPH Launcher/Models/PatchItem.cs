namespace RagnaPH.Launcher.Models;

public sealed class PatchItem
{
    public int Id { get; init; }
    public string FileName { get; init; } = "";
    public string RelativePath { get; init; } = ""; // e.g., "data/patch1.0 - item description fix.thor"
}

