using Azure;
using Azure.AI.OpenAI;

namespace azureai.src;

internal class AIClient
{
    private readonly OpenAIClient _openAIClient;

    private readonly string _deploymentName;

    public AIClient(OpenAIClient openAIClient, string deploymentName)
    {
        _openAIClient = openAIClient;
        _deploymentName = deploymentName;
    }

    public Task<Response<ChatCompletions>> GetChatCompletionsAsync(ChatCompletionsOptions chatCompletionsOptions)
    {
        return _openAIClient.GetChatCompletionsAsync(_deploymentName, chatCompletionsOptions);
    }

    public async Task<string> GetSingleFunctionCompletionAsync(FunctionDefinition functionDefinition, string userPrompt, string? systemMessage = null)
    {
        var history = new List<ChatMessage>();

        if (systemMessage != null)
        {
            history.Add(new ChatMessage(ChatRole.System, systemMessage));
        }

        history.Add(new ChatMessage(ChatRole.User, userPrompt));

        var options = new ChatCompletionsOptions(history)
        {
            // NucleusSamplingFactor = 0.95F,
            Temperature = 0.7F,
            MaxTokens = 800,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
            Functions = new[] { functionDefinition }, // still needed, even if we populate FunctionCall?
            FunctionCall = functionDefinition,
        };

        Response<ChatCompletions> response = await GetChatCompletionsAsync(options);
        ChatChoice completion = response.Value.Choices.First();

        return completion.Message.FunctionCall.Arguments;
    }

    public async Task<string> GetSingleCompletionAsync(string prompt, string? systemMessage = null)
    {
        var history = new List<ChatMessage>();

        if (systemMessage != null)
        {
            history.Add(new ChatMessage(ChatRole.System, systemMessage));
        }

        history.Add(new ChatMessage(ChatRole.User, prompt));

        var options = new ChatCompletionsOptions(history)
        {
            // NucleusSamplingFactor = 0.95F,
            Temperature = 0.7F,
            MaxTokens = 800,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
        };

        Response<ChatCompletions> response = await GetChatCompletionsAsync(options);
        ChatChoice completion = response.Value.Choices.First();

        return completion.Message.Content;
    }
}
