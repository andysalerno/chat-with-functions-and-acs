using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Search.Query;

internal class MicrosoftSearchClient
{
    private const string ClientId = "c5ec555a-d7ae-4106-a665-bf55a14e8a9d";

    private readonly GraphServiceClient _graphServiceClient;

    public MicrosoftSearchClient()
    {
        _graphServiceClient = new GraphServiceClient(
            new InteractiveBrowserCredential(
                options: new InteractiveBrowserCredentialOptions { ClientId = ClientId }),
            scopes: new[] { "User.Read", "Sites.Read.All", "Files.Read.All", "Mail.Read" });
    }

    public async Task InitializeAsync()
    {
        // Trigger a request to freshen up the token.
        await _graphServiceClient.Me.Messages.GetAsync();
    }

    public async Task<EmailContent[]> SearchEmailsAsync(string text)
    {
        var result = await _graphServiceClient.Search.Query.PostAsync(new QueryPostRequestBody
        {
            Requests =
                new List<SearchRequest>()
                {
                    new SearchRequest
                    {
                        // EntityTypes = new List<EntityType?>() { EntityType.ListItem, EntityType.DriveItem, EntityType.Message },
                        EntityTypes = new List<EntityType?>() { EntityType.Message },
                        Query = new SearchQuery
                        {
                            QueryString = text,
                        },
                        EnableTopResults = true,
                        From = 0,
                        Size = 3,
                    },
                },
        });

        IEnumerable<string?> messageIds = result?
            .Value?
            .First()?
            .HitsContainers?
            .SelectMany(c => c.Hits ?? Enumerable.Empty<SearchHit>())
            .Where(hit => hit.Resource is Message)
            .Select(h => h.HitId)
            .Where(id => id != null)
            ?? throw new InvalidOperationException("No hits found");

        var getEmailsTasks = messageIds.Select(async id => await GetEmailAsync(_graphServiceClient, id ?? string.Empty)).ToList();

        var emails = await Task.WhenAll(getEmailsTasks);

        return emails;
    }

    public async Task<DocumentContent[]> SearchOneDriveAsync(string text)
    {
        var result = await _graphServiceClient.Search.Query.PostAsync(new QueryPostRequestBody
        {
            Requests =
                new List<SearchRequest>()
                {
                    new SearchRequest
                    {
                        EntityTypes = new List<EntityType?>() { EntityType.DriveItem },
                        Query = new SearchQuery
                        {
                            QueryString = text,
                        },
                        From = 0,
                        Size = 3,
                    },
                },
        });

        IEnumerable<(string HitId, string DriveId)> driveItemIds = result?
            .Value?
            .First()?
            .HitsContainers?
            .SelectMany(c => c.Hits ?? Enumerable.Empty<SearchHit>())
            .Where(hit => hit.Resource is DriveItem)
            .Select(h => (h.HitId ?? string.Empty, (h.Resource as DriveItem)?.ParentReference?.DriveId ?? string.Empty))
            ?? throw new InvalidOperationException("No hits found");

        foreach (var (hitId, driveId) in driveItemIds)
        {
            var documentContent = await GetDocumentContentAsync(_graphServiceClient, driveId, hitId);
        }

        // var getEmailsTasks = messageIds.Select(async id => await GetEmailAsync(_graphServiceClient, id ?? string.Empty)).ToList();
        // var emails = await Task.WhenAll(getEmailsTasks);
        return Array.Empty<DocumentContent>();
    }

    private static async Task<EmailContent> GetEmailAsync(GraphServiceClient client, string id)
    {
        var email = await client.Me.Messages[id].GetAsync((requestConfiguration) =>
        {
            requestConfiguration.QueryParameters.Select = new string[] { "subject", "body", "bodyPreview", "uniqueBody", "sentDateTime" };
            requestConfiguration.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
        });

        return new EmailContent
        {
            Body = email?.Body?.Content ?? string.Empty,
            Subject = email?.Subject ?? string.Empty,
            SentDate = email?.SentDateTime ?? DateTimeOffset.MinValue,
        };
    }

    private static async Task<DocumentContent> GetDocumentContentAsync(GraphServiceClient client, string driveId, string docId)
    {
        // var documentContent = await client.Drives[driveId].Items[docId].Content.GetAsync((requestConfiguration) =>
        var documentContent = await client.Drives[driveId].Items[docId].SearchWithQ(q: "my query").GetAsync(config =>
        {
        });

        // What about: "I found some documents that might help. Shoud I read one for you?"
        return new DocumentContent();
    }
}

internal class EmailContent
{
    public DateTimeOffset SentDate { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;
}

internal class DocumentContent
{
}
