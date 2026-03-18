using System.Collections.Generic;
using SimpleJSON;
using JsonNode = SimpleJSON.JSONNode;

namespace Runtime.Infrastructure.Save.Serialization
{
    internal static class SaveDataEnvelopeFields
    {
        internal const string VersionKey = "version";
        internal const string SectionsKey = "sections";
    }

    internal sealed class SaveDataEnvelopeMapper
    {
        public SaveData ParseRoot(JsonNode root, int version)
        {
            var parsed = new SaveData
            {
                Version = version,
                Sections = new Dictionary<string, JsonNode>(),
            };

            var rootObject = root.AsObject;
            
            if (rootObject == null || !rootObject.HasKey(SaveDataEnvelopeFields.SectionsKey))
                return parsed;

            var sectionsObject = rootObject[SaveDataEnvelopeFields.SectionsKey].AsObject;
            
            if (sectionsObject == null)
                return parsed;

            foreach (var sectionPair in sectionsObject)
            {
                parsed.Sections[sectionPair.Key] = sectionPair.Value;
            }

            return parsed;
        }

        public JsonNode BuildRoot(SaveData saveData)
        {
            var root = new JSONObject
            {
                [SaveDataEnvelopeFields.VersionKey] = saveData.Version,
            };

            var sections = new JSONObject();
            
            foreach (var sectionPair in saveData.Sections)
            {
                sections[sectionPair.Key] = sectionPair.Value;
            }

            root[SaveDataEnvelopeFields.SectionsKey] = sections;
            return root;
        }
    }
}