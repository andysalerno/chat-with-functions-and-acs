namespace Functions;

using Azure.AI.OpenAI;

internal interface IFunction
{
    string FunctionName { get; }

    FunctionDefinition FunctionDefinition { get; }

    Task<FunctionResult> InvokeAsync(FunctionCall call);
}
