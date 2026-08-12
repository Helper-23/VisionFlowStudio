using System.IO;
using System.Runtime.Serialization.Json;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.App
{
    public static class FlowDocumentStore
    {
        public static FlowDocument Load(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(FlowDocument));
            using (var stream = File.OpenRead(path))
                return (FlowDocument)serializer.ReadObject(stream);
        }

        public static bool HasStationRecipeContext(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            var text = File.ReadAllText(path);
            return text.IndexOf("\"StationName\"", System.StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf("\"RecipeName\"", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Save(FlowDocument document, string path)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);
            var serializer = new DataContractJsonSerializer(
                typeof(FlowDocument),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using (var stream = File.Create(path))
                serializer.WriteObject(stream, document);
        }
    }
}
