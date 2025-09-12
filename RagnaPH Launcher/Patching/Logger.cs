using System;
using System.IO;

namespace RagnaPH.Patching;

internal static class Logger
{
    private static readonly object _lock = new();

    public static void Log(string message)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"launcher-{DateTime.UtcNow:yyyyMMdd}.txt");
        var line = $"{DateTime.UtcNow:O} {message}";
        lock (_lock)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
