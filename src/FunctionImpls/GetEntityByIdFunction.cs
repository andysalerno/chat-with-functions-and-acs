using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using azureai.src.Dataverse;
using Functions;

internal class GetEntityByIdFunction : IFunction
{
    private readonly DataverseClient _dataverseClient;

    public GetEntityByIdFunction(DataverseClient dataverseClient) => _dataverseClient = dataverseClient;

    public string FunctionName => "get_entity_by_guid";

    public FunctionDefinition FunctionDefinition =>
        new FunctionBuilder(FunctionName)
            .WithDescription("Retrieve an entity as json, given a guid of its ID.")
            .WithParameter(FieldNames.EntityId, FunctionBuilder.Type.String, "The guid ID of the entity", isRequired: true)
            .WithEnumParameter(FieldNames.EntityType, "The type of entity to query", new[] { "msdyn_workorders", "msdyn_bookings" }, isRequired: true)
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall call)
    {
        Parameters parameters = JsonSerializer.Deserialize<Parameters>(call.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as Parameters.");

        if (!Guid.TryParse(parameters.EntityId, out _))
        {
            return new FunctionResult(
                isSuccess: false,
                new { error = "The given ID was not a valid guid. This function can only be called on valid guids. Try searching a different way." });
        }

        string entityJson = await _dataverseClient.GetEntityJsonByIdAsync(parameters.EntityType, parameters.EntityId);

        // Bit of a hack: remove null values from the json, since they make up a large portion of the json and waste tokens.
        {
            Dictionary<string, object> asDict = JsonSerializer.Deserialize<Dictionary<string, object>>(entityJson) ?? throw new Exception("Could not parse entity json.");

            foreach (string key in asDict.Keys.ToList())
            {
                if (asDict[key] == null)
                {
                    _ = asDict.Remove(key);
                }
            }

            entityJson = JsonSerializer.Serialize(asDict);
        }

        return new FunctionResult(isSuccess: true, entityJson);
    }

    internal class Parameters
    {
        [JsonPropertyName(FieldNames.EntityId)]
        public string EntityId { get; set; } = string.Empty;

        [JsonPropertyName(FieldNames.EntityType)]
        public string EntityType { get; set; } = string.Empty;
    }

    private static class FieldNames
    {
        public const string EntityId = "entity_id";

        public const string EntityType = "entity_type";
    }
}
