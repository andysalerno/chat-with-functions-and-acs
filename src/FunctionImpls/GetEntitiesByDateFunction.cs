using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using azureai.src;
using azureai.src.Dataverse;
using Functions;

internal class GetEntitiesByDateFunction : IFunction
{
    private readonly DataverseClient _dataverseClient;

    public GetEntitiesByDateFunction(DataverseClient dataverseClient) => _dataverseClient = dataverseClient;

    public string FunctionName => "query_entities_by_date";

    public FunctionDefinition FunctionDefinition =>
        new FunctionBuilder(FunctionName)
            .WithDescription("Query entities by a range of time")
            .WithEnumParameter(FieldNames.EntityName, "The type of entity to query for", new[] { "msdyn_workorders", "msdyn_bookings" }, isRequired: true)
            .WithParameter(FieldNames.NotBeforeUtc, FunctionBuilder.Type.String, "Exclude results before this date. E.x. '1997-07-16T19:20Z'. Required if not_after_utc is empty.", isRequired: false)
            .WithParameter(FieldNames.NotAfterUtc, FunctionBuilder.Type.String, "Exclude results after this date. E.x. '1997-07-16T19:20Z'. Required if not_before_utc is empty.", isRequired: false)
            .WithEnumParameter(FieldNames.DateFieldName, "The datetime field name to compare against", new[] { "date_modified", "date_created", "date_start", "date_end" }, isRequired: true)
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall call)
    {
        Parameters parameters = JsonSerializer.Deserialize<Parameters>(call.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as GetWorkOrderParameters.");

        DateTime? notBeforeUtc = string.IsNullOrEmpty(parameters.NotBeforeUtc) ? null : DateTime.Parse(parameters.NotBeforeUtc);
        DateTime? notAfterUtc = string.IsNullOrEmpty(parameters.NotAfterUtc) ? null : DateTime.Parse(parameters.NotAfterUtc);

        DataverseClient.TargetField targetField = parameters.DateFieldName switch
        {
            "date_modified" => DataverseClient.TargetField.ModifiedOn,
            "date_created" => DataverseClient.TargetField.CreatedOn,
            "date_start" => DataverseClient.TargetField.WindowStart,
            "date_end" => DataverseClient.TargetField.WindowEnd,
            _ => throw new InvalidOperationException($"Invalid target field: {parameters.DateFieldName}"),
        };

        IReadOnlyList<string> workOrderJson = await _dataverseClient.GetEntitiesByDateAsync(parameters.EntityName, notBeforeUtc, notAfterUtc, targetField);

        var removedNull = workOrderJson.Select(j => JsonUtil.RemoveNullValues(j)).ToList();

        var asJsonDocs = removedNull.ConvertAll(item => JsonDocument.Parse(item));

        return new FunctionResult(isSuccess: true, asJsonDocs);
    }

    internal class Parameters
    {
        [JsonPropertyName(FieldNames.EntityName)]
        public string EntityName { get; set; } = string.Empty;

        [JsonPropertyName(FieldNames.NotBeforeUtc)]
        public string NotBeforeUtc { get; set; } = string.Empty;

        [JsonPropertyName(FieldNames.NotAfterUtc)]
        public string NotAfterUtc { get; set; } = string.Empty;

        [JsonPropertyName(FieldNames.DateFieldName)]
        public string DateFieldName { get; set; } = string.Empty;
    }

    private static class FieldNames
    {
        public const string EntityName = "entity_name";
        public const string NotBeforeUtc = "not_before_utc";
        public const string NotAfterUtc = "not_after_utc";
        public const string DateFieldName = "date_field_name";
    }
}
