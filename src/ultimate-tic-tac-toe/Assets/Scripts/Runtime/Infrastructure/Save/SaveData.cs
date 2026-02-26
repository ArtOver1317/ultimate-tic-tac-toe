using System.Collections.Generic;
using JsonNode = SimpleJSON.JSONNode;

namespace Runtime.Infrastructure.Save
{
    internal sealed class SaveData
    {
        public int Version { get; set; }
        public Dictionary<string, JsonNode> Sections { get; set; } = new();
    }
}