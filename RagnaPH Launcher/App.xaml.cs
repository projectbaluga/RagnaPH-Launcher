using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace RagnaPH_Launcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                string? resource = name switch
                {
                    var n when n.Equals("ICSharpCode.SharpZipLib", StringComparison.OrdinalIgnoreCase)
                        => "RagnaPH.Libs.ICSharpCode.SharpZipLib.dll",
                    // Optional: resolve the GRFEditor library when embedded. If
                    // the resource is missing the normal probing rules apply
                    // which allows the launcher to run without bundling the
                    // dependency.
                    var n when n.Equals("GRF", StringComparison.OrdinalIgnoreCase)
                        => "RagnaPH.Libs.GRF.dll",
                    _ => null
                };

                if (resource == null)
                    return null;

                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
                if (stream == null)
                    return null;

                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return Assembly.Load(ms.ToArray());
            };
        }
    }
}
