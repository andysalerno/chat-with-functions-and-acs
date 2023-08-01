using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using azureai.src.AzureCognitiveSearch;
using Functions;

namespace azureai.src.FunctionImpls;

internal class SearchAzureCognitiveSearchFunction : IFunction
{
    private readonly AzureCognitiveSearchClient _searchClient;

    public SearchAzureCognitiveSearchFunction(AzureCognitiveSearchClient searchClient) => _searchClient = searchClient;

    public string FunctionName => "search_documents";

    public FunctionDefinition FunctionDefinition =>
        new FunctionBuilder(FunctionName)
            .WithDescription("Search reference documents (manuals, instruction booklets, technical briefs) using a query in plain English")
            .WithParameter(FieldNames.PlainTextQuery, FunctionBuilder.Type.String, "The question, in plain English text", isRequired: true)
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall call)
    {
        Parameters parameters = JsonSerializer.Deserialize<Parameters>(call.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as Parameters.");

        var results = await _searchClient.SearchWithSemanticSearch(parameters.PlainTextQuery);

        return new FunctionResult(isSuccess: true, "result");
    }

    private static class FieldNames
    {
        public const string PlainTextQuery = "PlainTextQuestion";
    }

    private class Parameters
    {
        [JsonPropertyName(FieldNames.PlainTextQuery)]
        public string PlainTextQuery { get; set; } = string.Empty;
    }
}
