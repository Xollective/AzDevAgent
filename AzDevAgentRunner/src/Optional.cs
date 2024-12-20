namespace AzDevAgentRunner;

public record struct Optional<T>(T? Value, bool HasValue = true)
{
    public static implicit operator Optional<T>(T? value) => new(value);
}
