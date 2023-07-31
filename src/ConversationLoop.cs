using Azure;
using Azure.AI.OpenAI;
using azureai.src;
using Functions;
using Microsoft.Extensions.Logging;
using static azureai.src.LoggerProvider;

internal class ConversationLoop
{
    /// <summary>
    /// The functions that OpenAI can invoke.
    /// </summary>
    private readonly IReadOnlyList<IFunction> _functions;

    private readonly AIClient _openAIClient;

    private readonly List<ChatMessage> _history;

    public ConversationLoop(AIClient openAIClient, IReadOnlyList<IFunction> functions)
    {
        _openAIClient = openAIClient;

        _functions = functions;

        // Add the system message that starts the conversation:
        _history = new List<ChatMessage>
        {
            GetSystemMessage(),
        };
    }

    public async Task RunSessionLoopAsync()
    {
        while (true)
        {
            // User turn: simply get the message
            ChatMessage userNextMessage = GetUserChatMessage();
            _history.Add(userNextMessage);

            // Assistant turn is a loop:
            // 1. Get the message. If normal content message, done.
            while (true)
            {
                // Get the message.
                ChatMessage nextAIMessage = await GetAIChatResponseAsync(_history);
                _history.Add(nextAIMessage);

                // No function to call, so we're done with this turn.
                if (nextAIMessage.FunctionCall == null)
                {
                    Logger.LogInformation($"Assistant: {nextAIMessage.Content}");

                    break;
                }

                // If the message is a function call, invoke it.
                var call = nextAIMessage.FunctionCall;
                FunctionResult result = await InvokeFunctionAsync(call);

                string json = result.ToJson();
                _history.Add(new ChatMessage(ChatRole.Function, json) { Name = call.Name });

                if (result.IsSuccess == false)
                {
                    Logger.LogError($"Function call failed: {json}");
                }
            }
        }
    }

    private static ChatCompletionsOptions CreateChatCompletionsOptions(List<ChatMessage> history, IReadOnlyList<IFunction> availableFunctions)
    {
        var options = new ChatCompletionsOptions(history)
        {
            // NucleusSamplingFactor = 0.95F,
            Temperature = 0.7F,
            MaxTokens = 800,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            Functions = availableFunctions.Select(f => f.FunctionDefinition).ToList(),
        };

        return options;
    }

    private static ChatMessage GetSystemMessage()
    {
        string systemPrompt = File.ReadAllText("systemprompt.txt").Trim();
        systemPrompt = systemPrompt.Replace("{{timestamp}}", DateTimeOffset.UtcNow.ToString());
        return new ChatMessage(ChatRole.System, systemPrompt);
    }

    private static ChatMessage GetUserChatMessage()
    {
        Logger.LogInformation("You: ");
        string text = Console.ReadLine() ?? string.Empty;

        var message = new ChatMessage(ChatRole.User, text);
        return message;
    }

    private async Task<FunctionResult> InvokeFunctionAsync(FunctionCall functionCall)
    {
        Logger.LogInformation($"Function call requested: {functionCall.Name}({functionCall.Arguments})");

        if (_functions.FirstOrDefault(f => f.FunctionName == functionCall.Name) is IFunction function)
        {
            Logger.LogInformation($"Found matching function name: {functionCall.Name}");
            FunctionResult result = await function.InvokeAsync(functionCall);

            return result;
        }

        throw new InvalidOperationException($"Function name not recognized: {functionCall.Name}");
    }

    private async Task<ChatMessage> GetAIChatResponseAsync(List<ChatMessage> history)
    {
        Response<ChatCompletions> response = await _openAIClient.GetChatCompletionsAsync(
            chatCompletionsOptions: CreateChatCompletionsOptions(history, _functions));

        return response.Value.Choices.First().Message;
    }
}
