using System.Text.Json.Serialization;

namespace azureai.src.AzureCognitiveSearch;

internal class SemanticSearchResponse
{
    [JsonPropertyName("likely_answer")]
    public List<string>? LikelyAnswers { get; set; }

    [JsonPropertyName("relevant_excerpts")]
    public List<string> RelevantExcerpts { get; } = new List<string>();
}
