namespace RagnaPH.Patching;

public interface IMirrorSelector
{
    PatchServer Current { get; }
    PatchServer NextOnFailure();
}
