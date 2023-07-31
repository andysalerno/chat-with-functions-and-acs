namespace azureai.src;

using System.Text;

internal class DocumentChunker
{
    private readonly int _maxChunkSize;

    private readonly int _overlapSize;

    public DocumentChunker(int maxChunkSize, int overlapSize = 0)
    {
        _maxChunkSize = maxChunkSize;
        _overlapSize = overlapSize;
    }

    public List<Chunk> ChunkDocument(string documentContent)
    {
        // 1. First, detect the correct newline character.
        string newline = "\n";

        if (documentContent.Contains("\r\n"))
        {
            newline = "\r\n";
        }

        // 2. Then, split into lines, and then into sentences.
        // This can be destructive and isn't perfect but it gives good results.
        var splits = documentContent
            .Split(newline)
            .SelectMany(line =>
            {
                var sentenceSplits = line.Split(". ");

                // Add the ". " back in to each split (except the last one)
                for (int i = 0; i < sentenceSplits.Length - 1; i++)
                {
                    sentenceSplits[i] += ". ";
                }

                return sentenceSplits;
            })
            .SelectMany(chunk =>
            {
                // If the chunk is still too long, we simply split it in half.
                if (chunk.Length > _maxChunkSize)
                {
                    var half = chunk.Length / 2;
                    return new[] { chunk.Substring(0, half), chunk.Substring(half) };
                }

                return new[] { chunk };
            });

        var chunks = new List<Chunk>();

        var sb = new StringBuilder();

        foreach (string split in splits)
        {
            if (sb.Length + split.Length < _maxChunkSize)
            {
                sb.Append(split);
            }
            else
            {
                chunks.Add(new Chunk(sb.ToString()));

                sb.Clear();
                sb.Append(split);
            }
        }

        return chunks;
    }

    public class Chunk
    {
        public Chunk(string content) => Content = content;

        public string Content { get; }
    }
}
