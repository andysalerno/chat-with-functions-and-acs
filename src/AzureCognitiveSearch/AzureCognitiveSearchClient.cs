using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;

namespace azureai.src.AzureCognitiveSearch;
internal class AzureCognitiveSearchClient
{
    private readonly SearchClient _searchClient;
    private readonly EmbeddingsClient _embeddingsClient;

    public AzureCognitiveSearchClient()
    {
        string key = Config.GetConfigurationValue("azureCognitiveSearchKey");
        string indexName = Config.GetConfigurationValue("indexName");
        string searchEndpoint = Config.GetConfigurationValue("searchEndpoint");
        string embeddingsEndpoint = Config.GetConfigurationValue("embeddingsEndpoint");

        // Initialize Azure Cognitive Search clients
        var searchCredential = new AzureKeyCredential(key);
        var indexClient = new SearchIndexClient(new Uri(searchEndpoint), searchCredential);

        _searchClient = indexClient.GetSearchClient(indexName);
        _embeddingsClient = new EmbeddingsClient(new Uri(embeddingsEndpoint));
    }

    public async Task IndexDocumentAsync(string documentTitle, string documentContent, string documentUri)
    {
        List<Embedding> embeddings = await _embeddingsClient.GetEmbeddingsAsync(new[] { documentContent });

        SearchDocument document = CreateDocument(documentTitle, documentUri, documentContent, embeddings[0]);

        _ = await _searchClient.IndexDocumentsAsync(IndexDocumentsBatch.Upload(new[] { document }));
    }

    public async Task IndexChunksAsync(string documentTitle, IReadOnlyCollection<DocumentChunker.Chunk> chunks, string documentUri)
    {
        List<Embedding> embeddings = await _embeddingsClient.GetEmbeddingsAsync(chunks.Select(c => c.Content).ToList());

        var documents = chunks
            .Zip(embeddings)
            .Select(tup => CreateDocument(documentTitle, documentUri, tup.First.Content, tup.Second)).ToList();

        Response<IndexDocumentsResult> result = await _searchClient.IndexDocumentsAsync(IndexDocumentsBatch.Upload(documents));

        Console.WriteLine($"Uploaded {documents.Count} documents for file {documentTitle}");
    }

    public async Task<IEnumerable<string>> SearchDocumentsAsync(string query, int kNearest = 3)
    {
        Embedding embedding = (await _embeddingsClient.GetEmbeddingsAsync(new[] { query })).Single();

        var vector = new SearchQueryVector { KNearestNeighborsCount = 3, Fields = "contentVector", Value = embedding.Values.ToArray() };
        var searchOptions = new SearchOptions
        {
            Vector = vector,
            Size = kNearest,
            Select = { "title", "documentUri", "id", "documentContent" },
        };

        // Interestingly, you can pass `query` as the first argument, to include the original text in the query. Need to explore when this is useful.
        // SearchResults<SearchDocument> response = await _searchClient.SearchAsync<SearchDocument>(query, searchOptions);
        SearchResults<SearchDocument> response = await _searchClient.SearchAsync<SearchDocument>(null, searchOptions);

        var documentUris = new List<string>();

        await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync())
        {
            string uri = result.Document["documentUri"].ToString() ?? throw new NullReferenceException("Expected a document URI on the result, but found none.");
            Console.WriteLine($"Found document with score: {result.Score}, uri: {uri}");
            documentUris.Add(uri);
        }

        return documentUris;
    }

    private static SearchDocument CreateDocument(string title, string uri, string content, Embedding embedding)
    {
        var document = new SearchDocument(new Dictionary<string, object>
        {
            { "id", Guid.NewGuid().ToString() },
            { "title", title },
            { "documentContent", content },
            { "contentVector", embedding.Values },
            { "documentUri", uri },
        });

        return document;
    }

    private async Task AddDocumentsToIndex()
    {
        var searchClient = new AzureCognitiveSearchClient();
        var chunker = new DocumentChunker(maxChunkSize: 1600);

        // Index document 1
        {
            string document1Content = await File.ReadAllTextAsync("TestDocuments/lennon.txt");

            var chunks = chunker.ChunkDocument(document1Content);

            await searchClient.IndexChunksAsync(
                documentTitle: "lennon",
                chunks: chunks,
                documentUri: "TestDocuments/lennon.txt");
        }

        // Index document 2
        {
            string document1Content = await File.ReadAllTextAsync("TestDocuments/mccartney.txt");

            var chunks = chunker.ChunkDocument(document1Content);

            await searchClient.IndexChunksAsync(
                documentTitle: "mccartney",
                chunks: chunks,
                documentUri: "TestDocuments/mccartney.txt");
        }
    }
}
