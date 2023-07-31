using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal class EmbeddingsClient : IEmbeddingsClient
{
    private readonly Uri _endpoint;

    public EmbeddingsClient(Uri endpoint) => _endpoint = endpoint;

    public async Task<List<Embedding>> GetEmbeddingsAsync(IEnumerable<string> texts)
    {
        // Gets embeddings from the endpoint at /embeddings.
        using var client = new HttpClient();

        var requestBody = new Request
        {
            Input = texts,
        };

        string serialized = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = _endpoint,
            Content = new StringContent(serialized, Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Request failed with status code {response.StatusCode}");
        }

        // Response shaped like:
        var responseContent = await response.Content.ReadAsStringAsync();

        var parsed = JsonSerializer.Deserialize<Response>(responseContent)
            ?? throw new InvalidOperationException("Could not parse response as Response.");

        return parsed.Data.ToList();
    }

    private class Request
    {
        [JsonPropertyName("input")]
        public IEnumerable<string> Input { get; init; } = Enumerable.Empty<string>();
    }

    private class Response
    {
        [JsonPropertyName("data")]
        public Embedding[] Data { get; set; } = Array.Empty<Embedding>();
    }
}
