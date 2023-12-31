﻿using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;

namespace azureai.src.AzureCognitiveSearch;
internal class AzureCognitiveSearchClient
{
    private readonly SearchClient _searchClient;
    private readonly EmbeddingsClient _embeddingsClient;

    public AzureCognitiveSearchClient(Uri endpoint, string indexName, string key)
    {
        string embeddingsEndpoint = Config.GetConfigurationValue("embeddingsEndpoint");

        // Initialize Azure Cognitive Search clients
        var credential = new AzureKeyCredential(key);
        var indexClient = new SearchIndexClient(endpoint, credential);

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

    public async Task<IEnumerable<string>> SearchWithEmbeddings(string query, int kNearest = 3)
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

    public async Task<SemanticSearchResponse> SearchWithSemanticSearch(string query)
    {
        var options = new SearchOptions
        {
            Size = 3,
            QueryType = SearchQueryType.Semantic,
            QueryLanguage = QueryLanguage.EnUs,
            SemanticConfigurationName = "ansalernsemanticconfig",
            QueryCaption = QueryCaptionType.Extractive,
            QueryAnswer = QueryAnswerType.Extractive,
        };
        SearchResults<SearchDocument> response = await _searchClient.SearchAsync<SearchDocument>(query, options);

        var finalResult = new SemanticSearchResponse();

        // In the very best cases, Semantic Search is confident it found some answers for us,
        // and will populate the Answers property:
        if (response.Answers?.Any() == true)
        {
            finalResult.LikelyAnswers = response.Answers.Select(a => a.Text).ToList();
        }

        // In all cases, any results are represented in the captions property:
        await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync())
        {
            string documentName = result.Document["metadata_storage_name"].ToString() ?? throw new NullReferenceException("Expected a document name on the result, but found none.");
            Console.WriteLine($"Found document with score: {result.Score}, named: {documentName}");

            finalResult.RelevantExcerpts.AddRange(result.Captions.Select(c => c.Text));
        }

        return finalResult;
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
        // Intentionally broken for now:
        var searchClient = new AzureCognitiveSearchClient(null!, null!, null!);
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
