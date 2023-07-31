namespace FunctionImpls;

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Functions;

internal class SearchGraphFunction : IFunction
{
    private const string OneDriveSource = "documents";

    private const string OutlookSource = "emails";

    private readonly MicrosoftSearchClient _client;

    public SearchGraphFunction(MicrosoftSearchClient client) => _client = client;

    public string FunctionName => "search_graph";

    public FunctionDefinition FunctionDefinition =>
        new FunctionBuilder(FunctionName)
            .WithDescription("Search Microsoft Graph for documents and emails, given a query string")
            .WithParameter("query", FunctionBuilder.Type.String, "The short, terse query string, no more than 3 words", isRequired: true)
            .WithEnumParameter("preferred_source", "An enum; the data source most likely to have the best result", new[] { OneDriveSource, OutlookSource }, isRequired: true)
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall functionCall)
    {
        var parameters = JsonSerializer.Deserialize<GraphSearchParameters>(functionCall.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as EmailSearchParameters.");

        if (parameters.PreferredSource == OneDriveSource)
        {
            var documents = await _client.SearchOneDriveAsync(parameters.Query);
        }
        else if (parameters.PreferredSource == OutlookSource)
        {
            var emails = await _client.SearchEmailsAsync(parameters.Query);
        }
        else
        {
            throw new InvalidOperationException($"Unknown preferred source: {parameters.PreferredSource}");
        }

        return new FunctionResult(
            isSuccess: true,
            new
            {
            });
    }

    internal class GraphSearchParameters
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("preferred_source")]
        public string PreferredSource { get; set; } = string.Empty;
    }
}
