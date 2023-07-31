using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using azureai.src.Dataverse;
using Functions;

namespace azureai.src.FunctionImpls;
internal class SearchDataverseFunction : IFunction
{
    private readonly DataverseClient _dataverseClient;

    public SearchDataverseFunction(DataverseClient dataverseClient) => _dataverseClient = dataverseClient;

    public string FunctionName => "keyword_search_dataverse";

    public FunctionDefinition FunctionDefinition =>
        new FunctionBuilder(FunctionName)
            .WithDescription("Search Dataverse using a fuzzy keyword string query")
            .WithParameter(FieldNames.SearchQuery, FunctionBuilder.Type.String, "The keyword search query; filters NOT supported", isRequired: true)
            // .WithEnumParameter(FieldNames.EntityType, "Optional. Limit results to these entity types", new[] { "msdyn_workorder", "msdyn_booking" }, isRequired: false)
            .WithEnumParameter(FieldNames.EntityType, "Optional. Limit results to these entity types", new[] { "workorder" }, isRequired: false)
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall call)
    {
        Parameters parameters = JsonSerializer.Deserialize<Parameters>(call.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as Parameters.");

        string? entityName = null;

        if (parameters.EntityName.ToLower().Contains("workorder"))
        {
            entityName = "msdyn_workorder";
        }

        // else if (parameters.EntityName.ToLower().Contains("booking"))
        // {
        //    entityName = "bookableresourcebooking";
        // }
        string workOrderJson = await _dataverseClient.SearchDataverse(new DataverseClient.RelevancySearchQuery());

        return new FunctionResult(isSuccess: true, workOrderJson);
    }

    private static class FieldNames
    {
        public const string SearchQuery = "keyword_search_query";

        public const string EntityType = "entity_type";
    }

    private class Parameters
    {
        [JsonPropertyName(FieldNames.SearchQuery)]
        public string SearchQuery { get; set; } = string.Empty;

        [JsonPropertyName(FieldNames.EntityType)]
        public string EntityName { get; set; } = string.Empty;
    }
}
