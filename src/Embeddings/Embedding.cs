using System.Text.Json.Serialization;

public class Embedding
{
    [JsonPropertyName("embedding")]
    public float[] Values { get; set; } = Array.Empty<float>();
}
