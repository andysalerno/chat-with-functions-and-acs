namespace FunctionImpls;

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Functions;

internal class SearchOneDriveFunction : IFunction
{
    private readonly MicrosoftSearchClient _client;

    public SearchOneDriveFunction(MicrosoftSearchClient client) => _client = client;

    public string FunctionName => "search_onedrive";

    public FunctionDefinition FunctionDefinition =>
        new FunctionBuilder(FunctionName)
            .WithDescription("Search OneDrive for files and documents given a query string")
            .WithParameter("query", FunctionBuilder.Type.String, "The query string; should be no longer than two words.", isRequired: true)
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall functionCall)
    {
        var parameters = JsonSerializer.Deserialize<OneDriveSearchParameters>(functionCall.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as OneDriveSearchParameters.");

        await _client.SearchOneDriveAsync(parameters.Query);

        return new FunctionResult(
            isSuccess: true,
            new
            {
            });
    }

    internal class OneDriveSearchParameters
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;
    }
}
