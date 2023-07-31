using Azure;
using Azure.AI.OpenAI;
using azureai.src;
using azureai.src.Dataverse;
using azureai.src.FunctionImpls;
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

// Define the functions that will be available to the model:
var functions = new List<IFunction>
{
     // new GetEntitiesByDateFunction(dataverseClient),
     // new SearchDataverseFunction(dataverseClient),
     // new GetEntityByIdFunction(dataverseClient),
     // new DataverseRelevancySearchFunction(aiClient, dataverseClient),
     new GetEntityByRelevancySearchFunction(aiClient, dataverseClient),
};

var conversationLoop = new ConversationLoop(aiClient, functions);

await conversationLoop.RunSessionLoopAsync();
