namespace RagnaPH.Patching;

public interface IPatchEvents
{
    void OnStart(int total);
    void OnDownloading(int done, int total, long bytesPerSecond);
    void OnInstalling(int done, int total);
    void OnManualApplied(string fileName);
    void OnReady();
    void OnError(string message);
}
