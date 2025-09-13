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
                if (!name.Equals("ICSharpCode.SharpZipLib", StringComparison.OrdinalIgnoreCase))
                    return null;

                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("RagnaPH.Libs.ICSharpCode.SharpZipLib.dll");
                if (stream == null)
                    return null;

                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return Assembly.Load(ms.ToArray());
            };
        }
    }
}
