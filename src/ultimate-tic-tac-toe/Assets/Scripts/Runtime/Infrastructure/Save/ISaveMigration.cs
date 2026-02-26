using JsonNode = SimpleJSON.JSONNode;

namespace Runtime.Infrastructure.Save
{
    public interface ISaveMigration
    {
        int FromVersion { get; }
        JsonNode Migrate(JsonNode root);
    }
}