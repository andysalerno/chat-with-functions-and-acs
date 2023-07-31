namespace Functions;

using System.Text.Json;

internal class FunctionResult
{
    private readonly object _inner;

    public FunctionResult(bool isSuccess, object inner)
    {
        IsSuccess = isSuccess;
        _inner = inner;
    }

    public bool IsSuccess { get; }

    public string ToJson()
    {
        // Bit of a hack: if _inner is already a string, just return it.
        // Should we validate if the string is json? For now, no.
        if (_inner is string s)
        {
            return s;
        }

        return JsonSerializer.Serialize(_inner);
    }
}
