public interface IEmbeddingsClient
{
    Task<List<Embedding>> GetEmbeddingsAsync(IEnumerable<string> texts);
}
