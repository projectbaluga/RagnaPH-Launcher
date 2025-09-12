using System.Web.Script.Serialization;

namespace Newtonsoft.Json
{
    /// <summary>
    /// Minimal shim for JsonConvert using JavaScriptSerializer to avoid external dependencies.
    /// </summary>
    public static class JsonConvert
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static T? DeserializeObject<T>(string json) =>
            Serializer.Deserialize<T>(json);

        public static string SerializeObject(object value) =>
            Serializer.Serialize(value);
    }
}
