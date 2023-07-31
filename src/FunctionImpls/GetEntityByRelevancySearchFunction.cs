using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using azureai.src.Dataverse;
using Functions;
using Microsoft.Extensions.Logging;
using static azureai.src.LoggerProvider;

namespace azureai.src.FunctionImpls;

/// <summary>
/// A function for the following scenario:
/// USER: "What's the address of Work Order 12345?"
/// </summary>
internal class GetEntityByRelevancySearchFunction : IFunction
{
    private readonly AIClient _openAIClient;
    private readonly DataverseClient _dataverseClient;

    public GetEntityByRelevancySearchFunction(AIClient openAIClient, DataverseClient dataverseClient)
    {
        _openAIClient = openAIClient;
        _dataverseClient = dataverseClient;
    }

    public string FunctionName => "query_entity_via_plaintext";

    public FunctionDefinition FunctionDefinition =>
        new FunctionBuilder(FunctionName)
            .WithDescription("Search Dataverse using a query in plain English")
            .WithParameter(FieldNames.PlainTextQuery, FunctionBuilder.Type.String, "The query, in plain English text", isRequired: true)
            .WithParameter(FieldNames.NoteToUser, FunctionBuilder.Type.String, "A friendly note to the user about what you're doing, so they can sit tight while we work on their request.", isRequired: true)
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall call)
    {
        Parameters parameters = JsonSerializer.Deserialize<Parameters>(call.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as Parameters.");

        Logger.LogInformation(parameters.NoteToUser);

        // We have the query in plain text - now convince GPT to turn it into a real query.
        string query = parameters.PlainTextQuery;

        // The LLM turns the plain text query into a simpler query that will work in the relevancy search API:
        RelevancySearchQuery relevancySearchQuery = await GenerateRelevancySearchQueryAsync(query);

        // List of JSON objects:
        List<JsonDocument> searchResultsList = await SearchDataverseAsync(relevancySearchQuery);

        JsonDocument? fullEntityJson = await GetFullEntityFromResultAsync(searchResultsList);

        if (fullEntityJson == null)
        {
            return new FunctionResult(isSuccess: false, new { error = "The entity was not found." });
        }

        JsonDocument withNullValuesRemoved = JsonUtil.RemoveNullValues(fullEntityJson);

        string reserialized = JsonSerializer.Serialize(withNullValuesRemoved);

        return new FunctionResult(
            isSuccess: true,
            new
            {
                system_message = $"If you need a value but see a guid instead, try using the function get_entity_by_guid to expand it",
                content = reserialized,
            });
    }

    private async Task<JsonDocument?> GetFullEntityFromResultAsync(List<JsonDocument> searchResults)
    {
        // This is the ID of the entity with the highest search score:
        // string? topResultEntityId = searchResults.FirstOrDefault()?.GetValueOrDefault("@search.objectid")?.ToString();
        string? topResultEntityId = searchResults.FirstOrDefault()?.RootElement.GetProperty("@search.objectid").GetString();

        // string? topResultEntityName = searchResults.FirstOrDefault()?.GetValueOrDefault("@search.entityname")?.ToString();
        string? topResultEntityName = searchResults.FirstOrDefault()?.RootElement.GetProperty("@search.entityname").GetString();

        if (topResultEntityId == null || topResultEntityName == null)
        {
            return null;
        }

        JsonDocument result = await _dataverseClient.GetEntityJsonByIdAsync(topResultEntityName, topResultEntityId);

        return result;
    }

    private async Task<List<JsonDocument>> SearchDataverseAsync(RelevancySearchQuery query)
    {
        Logger.LogInformation($"Searching Dataverse Relevancy Search with query: {query.RelevancySearchQueryText}");

        var getResultsList = async (bool mustMatchAll) =>
        {
            // Get raw json:
            string dataverseResponseJson = await _dataverseClient.SearchDataverse(query.ToDataverseSearchQuery(), mustMatchAll);

            // Parse:
            var dataverseResponseDoc = JsonDocument.Parse(dataverseResponseJson);

            // Extract list of results:
            JsonElement searchResultsList = dataverseResponseDoc.RootElement.GetProperty("value");

            // Convert to list of dictionaries:
            List<JsonDocument> listOfObjs = searchResultsList.Deserialize<List<JsonDocument>>()
                ?? throw new Exception("Could not parse json.");

            return listOfObjs;
        };

        var searchResultsList = await getResultsList(true);

        if (!searchResultsList.Any())
        {
            // The original query may have been too narrow - let's try to broaden it.
            Logger.LogInformation("No results - broadening search and trying one more time.");
            searchResultsList = await getResultsList(false);
        }

        return searchResultsList;
    }

    private async Task<RelevancySearchQuery> GenerateRelevancySearchQueryAsync(string plainEnglishQuery)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset twoWeeksAgo = now - TimeSpan.FromDays(14);

        const string functionName = "query_dataverse_relevancy_api";

        string systemMessage =
            $"[TIMESTAMP UTC: {now:yyyy-MM-ddTHH:mmzzz}]\n" +
            "You are an assistant that can query Dataverse, given plaintext user queries.\n" +
            $"An example of invoking the function {functionName} is provided below.\n\n" +
            "EXAMPLE:\n" +
            "user input: 'unscheduled work orders for Contoso Coffee Co account'\n" +
            $"expected {FieldNames.RelevancySearchQuery}: 'Contoso Coffee Co unscheduled'\n\n" +
            "The next example demonstrates the optional date filtering:\n\n" +
            "EXAMPLE:\n" +
            "user input: 'accounts in atlanta created in the last two weeks'\n" +
            $"expected {FieldNames.RelevancySearchQuery}: 'Atlanta'\n" +
            $"expected {FieldNames.NotBeforeUtc}: '{twoWeeksAgo:yyyy-MM-ddTHH:mmzzz}'\n" +
            $"expected {FieldNames.DateFieldName}: 'createdon'";

        FunctionDefinition func =
            new FunctionBuilder(functionName)
                .WithDescription("Search using the Dataverse Relevancy API, a Lucene-based index search")
                .WithParameter(FieldNames.RelevancySearchQuery, FunctionBuilder.Type.String, "The keyword query text. Should be short and simple. Must not include entity names, only values.", isRequired: true)
                .WithParameter(FieldNames.NotBeforeUtc, FunctionBuilder.Type.String, "Optional. Exclude results before this date. E.x. '1997-07-16T19:20Z'", isRequired: false)
                .WithParameter(FieldNames.NotAfterUtc, FunctionBuilder.Type.String, "Optional. Exclude results after this date. E.x. '1997-07-16T19:20Z'", isRequired: false)
                .WithEnumParameter(FieldNames.DateFieldName, "The datetime field name to compare against. Required when not_before_utc or not_after_utc is set.", new[] { "createdon", "modifiedon" }, isRequired: false)
                .WithEnumParameter(FieldNames.EntityName, "Limit results to a particular entity type", new[] { "work_order" }, isRequired: false)
                .Build();

        string response = await _openAIClient.GetSingleFunctionCompletionAsync(
            func,
            plainEnglishQuery,
            systemMessage);

        RelevancySearchQuery relevancySearchQuery = JsonSerializer.Deserialize<RelevancySearchQuery>(response)
            ?? throw new InvalidOperationException("Could not parse response as RelevancySearchQuery.");

        if (relevancySearchQuery.EntityName == "work_order")
        {
            // Minor hack - the model does better without the "msdyn" prefix, but we need to fix it for the API to succeed.
            relevancySearchQuery.EntityName = "msdyn_workorder";
        }

        return relevancySearchQuery;
    }

    private static class FieldNames
    {
        public const string PlainTextQuery = "plain_text_query";
        public const string RelevancySearchQuery = "relevancy_search_query";
        public const string EntityName = "entity_name";
        public const string NotBeforeUtc = "not_before_utc";
        public const string NotAfterUtc = "not_after_utc";
        public const string DateFieldName = "date_field_name";
        public const string NoteToUser = "note_to_user";
    }

    private class Parameters
    {
        [JsonPropertyName(FieldNames.PlainTextQuery)]
        public string PlainTextQuery { get; set; } = string.Empty;

        [JsonPropertyName(FieldNames.NoteToUser)]
        public string NoteToUser { get; set; } = string.Empty;
    }

    private class RelevancySearchQuery
    {
        [JsonPropertyName(FieldNames.RelevancySearchQuery)]
        public string RelevancySearchQueryText { get; set; } = string.Empty;

        [JsonPropertyName(FieldNames.DateFieldName)]
        public string? DateFieldName { get; set; }

        [JsonPropertyName(FieldNames.NotBeforeUtc)]
        public DateTimeOffset? NotBeforeUtc { get; set; }

        [JsonPropertyName(FieldNames.NotAfterUtc)]
        public DateTimeOffset? NotAfterUtc { get; set; }

        [JsonPropertyName(FieldNames.EntityName)]
        public string? EntityName { get; set; }

        public DataverseClient.RelevancySearchQuery ToDataverseSearchQuery() => new()
        {
            RelevancySearchQueryText = RelevancySearchQueryText,
            DateFieldName = DateFieldName,
            NotBeforeUtc = NotBeforeUtc,
            NotAfterUtc = NotAfterUtc,
            EntityName = EntityName,
        };
    }
}
