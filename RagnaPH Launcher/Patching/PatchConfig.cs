using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace RagnaPHPatcher.Patching
{
    public class PatchConfig
    {
        public List<string> PatchServers { get; set; } = new List<string>();
        public string PatchList { get; set; } = "patches.txt";
        public string PatchDirectory { get; set; } = "patches";
        public string CacheFile { get; set; } = "patchcache.txt";

        public static PatchConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                return new PatchConfig();
            }

            var serializer = new JavaScriptSerializer();
            var json = File.ReadAllText(path);
            return serializer.Deserialize<PatchConfig>(json) ?? new PatchConfig();
        }
    }
}
