using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using azureai.src.Dataverse;
using Functions;

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
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall call)
    {
        Parameters parameters = JsonSerializer.Deserialize<Parameters>(call.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as Parameters.");

        // We have the query in plain text - now convince GPT to turn it into a real query.
        string query = parameters.PlainTextQuery;

        // The LLM turns the plain text query into a simpler query that will work in the relevancy search API:
        RelevancySearchQuery relevancySearchQuery = await GenerateRelevancySearchQueryAsync(query);

        // List of JSON objects:
        List<Dictionary<string, object>> searchResultsList = await SearchDataverseAsync(relevancySearchQuery);

        string? simplifiedEntity = await GetSimplifiedEntityFromResultAsync(searchResultsList);

        var reconstructedList = new List<Dictionary<string, object>>();

        foreach (Dictionary<string, object> obj in searchResultsList)
        {
            var reconstructedObj = new Dictionary<string, object>();

            foreach (KeyValuePair<string, object> kvp in obj)
            {
                if (kvp.Value != null)
                {
                    reconstructedObj.Add(kvp.Key, kvp.Value);
                }
            }

            reconstructedList.Add(reconstructedObj);
        }

        string reserialized = JsonSerializer.Serialize(reconstructedList);

        return new FunctionResult(isSuccess: true, reserialized);
    }

    private async Task<string?> GetSimplifiedEntityFromResultAsync(List<Dictionary<string, object>> searchResults)
    {
        // This is the ID of the entity with the highest search score:
        string? topResultEntityId = searchResults.FirstOrDefault()?.GetValueOrDefault("@search.objectid")?.ToString();
        string? topResultEntityName = searchResults.FirstOrDefault()?.GetValueOrDefault("@search.entityname")?.ToString();

        if (topResultEntityId == null || topResultEntityName == null)
        {
            return null;
        }

        string result = await _dataverseClient.GetEntityJsonByIdAsync(topResultEntityName, topResultEntityId);

        return result;
    }

    private async Task<List<Dictionary<string, object>>> SearchDataverseAsync(RelevancySearchQuery query)
    {
        Console.WriteLine($"Searching Dataverse Relevancy Search with query: {query.RelevancySearchQueryText}");

        var getResultsList = async (bool mustMatchAll) =>
        {
            // Get raw json:
            string dataverseResponseJson = await _dataverseClient.SearchDataverse(query.ToDataverseSearchQuery(), mustMatchAll);

            // Parse:
            var dataverseResponseDoc = JsonDocument.Parse(dataverseResponseJson);

            // Extract list of results:
            JsonElement searchResultsList = dataverseResponseDoc.RootElement.GetProperty("value");

            // Convert to list of dictionaries:
            List<Dictionary<string, object>> listOfObjs = searchResultsList.Deserialize<List<Dictionary<string, object>>>()
                ?? throw new Exception("Could not parse json.");

            return listOfObjs;
        };

        var searchResultsList = await getResultsList(true);

        if (!searchResultsList.Any())
        {
            // The original query may have been too narrow - let's try to broaden it.
            Console.WriteLine("No results - broadening search and trying one more time.");
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

    private async Task<IReadOnlyList<string>> GetRelevantTableNamesAsync(string query)
    {
        const string systemMessage = "You are an assistant that selects the correct tables to query based on the user's query text, and a list of table names. Your response is always a comma-separated list of the selected table names.";

        string combinedQuery = $"The following is a plaintext query, followed by a list of table names. Select the table names that are relevant to the query. \n\n{query}\n\nmsdyn_workorder\nbookableresourcebooking";

        string response = await _openAIClient.GetSingleCompletionAsync(combinedQuery, systemMessage);

        return response.Split(',');
    }

    private async Task<string> GetTableSchemaAsync(string tableName) => await _dataverseClient.GetTableSchemaAsync(tableName);

    private string BuildQueryFromSchemas(string plainTextQuery, IReadOnlyList<string> schemas) => "temp";

    private static class FieldNames
    {
        public const string PlainTextQuery = "plain_text_query";
        public const string RelevancySearchQuery = "relevancy_search_query";
        public const string EntityName = "entity_name";
        public const string NotBeforeUtc = "not_before_utc";
        public const string NotAfterUtc = "not_after_utc";
        public const string DateFieldName = "date_field_name";
    }

    private class Parameters
    {
        [JsonPropertyName(FieldNames.PlainTextQuery)]
        public string PlainTextQuery { get; set; } = string.Empty;
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
