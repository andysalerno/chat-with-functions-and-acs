using System.Text.Json;

namespace azureai.src;

public static class JsonUtil
{
    public static string RemoveNullValues(string json)
    {
        Dictionary<string, object> asDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? throw new Exception("Could not parse json.");

        foreach (string key in asDict.Keys.ToList())
        {
            if (asDict[key] == null)
            {
                _ = asDict.Remove(key);
            }
        }

        return JsonSerializer.Serialize(asDict);
    }

    public static JsonDocument RemoveNullValues(JsonDocument json)
    {
        string jsonString = JsonSerializer.Serialize(json);

        string nullValuesRemoved = RemoveNullValues(jsonString);

        return JsonDocument.Parse(nullValuesRemoved);
    }
}
