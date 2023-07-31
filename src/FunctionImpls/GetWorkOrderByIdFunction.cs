using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using azureai.src.Dataverse;
using Functions;

namespace azureai.src.FunctionImpls;
internal class GetWorkOrderByIdFunction : IFunction
{
    private readonly DataverseClient _dataverseClient;

    public GetWorkOrderByIdFunction(DataverseClient dataverseClient) => _dataverseClient = dataverseClient;

    public string FunctionName => "get_work_order_by_id";

    public FunctionDefinition FunctionDefinition =>
        new FunctionBuilder(FunctionName)
            .WithDescription("Retrieve a work order entity as json.")
            .WithParameter("work_order_id", FunctionBuilder.Type.String, "The ID of the work order", isRequired: true)
            .Build();

    public async Task<FunctionResult> InvokeAsync(FunctionCall call)
    {
        GetWorkOrderParameters parameters = JsonSerializer.Deserialize<GetWorkOrderParameters>(call.Arguments)
            ?? throw new InvalidOperationException("Could not parse arguments as GetWorkOrderParameters.");

        JsonDocument workOrderJson = await _dataverseClient.GetWorkOrderJsonByIdAsync(parameters.WorkOrderId);

        // Bit of a hack: remove null values from the json, since they make up a large portion of the json and waste tokens.
        string serialized;
        {
            Dictionary<string, object> asDict = JsonSerializer.Deserialize<Dictionary<string, object>>(workOrderJson) ?? throw new Exception("Could not parse work order json.");

            foreach (string key in asDict.Keys.ToList())
            {
                if (asDict[key] == null)
                {
                    _ = asDict.Remove(key);
                }
            }

            serialized = JsonSerializer.Serialize(asDict);
        }

        return new FunctionResult(
            isSuccess: true,
            serialized);
    }

    internal class GetWorkOrderParameters
    {
        [JsonPropertyName("work_order_id")]
        public string WorkOrderId { get; set; } = string.Empty;
    }
}
