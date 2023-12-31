﻿using Azure;
using Azure.AI.OpenAI;
using azureai.src;
using azureai.src.AzureCognitiveSearch;
using azureai.src.Dataverse;
using azureai.src.FunctionImpls;
using FunctionImpls;
using Functions;
using Microsoft.Extensions.Logging;
using static azureai.src.LoggerProvider;

Logger.LogInformation("Starting up...");

static string ReadKeyOpenAICredentialKeyFromFile() => File.ReadAllText(".key").Trim();

// Configure the OpenAI client:
var openAIClient = new OpenAIClient(
    new Uri(Config.GetConfigurationValue("openAIEndpointUri")),
    new AzureKeyCredential(ReadKeyOpenAICredentialKeyFromFile()));

var aiClient = new AIClient(openAIClient, "gpt35t");

var dataverseClient = new DataverseClient(
       baseUri: Config.GetConfigurationValue("dataverseUri"),
       clientAppId: Config.GetConfigurationValue("clientAppId"));

var microsoftGraphClient = new MicrosoftSearchClient();

var azureSearchClient = new AzureCognitiveSearchClient(
    new Uri(Config.GetConfigurationValue("searchEndpoint")),
    Config.GetConfigurationValue("indexName"),
    Config.GetConfigurationValue("azureCognitiveSearchKey"));

// Define the functions that will be available to the model:
var functions = new List<IFunction>
{
    new SearchAzureCognitiveSearchFunction(azureSearchClient),
    new SearchEmailFunction(microsoftGraphClient),
    new GetEntityByIdFunction(dataverseClient),
    new GetEntityByRelevancySearchFunction(aiClient, dataverseClient),
};

var conversationLoop = new ConversationLoop(aiClient, functions);

await conversationLoop.RunSessionLoopAsync();
