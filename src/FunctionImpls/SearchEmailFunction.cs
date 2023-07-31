namespace FunctionImpls;

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Functions;

internal class SearchEmailFunction : IFunction
{
    private readonly MicrosoftSearchClient _client;

    public SearchEmailFunction(MicrosoftSearchClient client) => _client = client;

    public string FunctionName => "search_email";

    public FunctionDefinition FunctionDefinition =>
        new FunctionBuilder(FunctionName)
            .WithDescription("Search Outlook for emails given a query string")
            .WithParameter("query", FunctionBuilder.Type.String, "The query string", isRequired: true)
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall functionCall)
    {
        var parameters = JsonSerializer.Deserialize<EmailSearchParameters>(functionCall.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as EmailSearchParameters.");

        var emails = await _client.SearchEmailsAsync(parameters.Query);

        foreach (var email in emails)
        {
            Console.WriteLine($"Email: {email.Subject}{email.SentDate}\n{email.Body}");
        }

        return new FunctionResult(
            isSuccess: true,
            new
            {
                emails,
            });
    }

    internal class EmailSearchParameters
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;
    }
}
