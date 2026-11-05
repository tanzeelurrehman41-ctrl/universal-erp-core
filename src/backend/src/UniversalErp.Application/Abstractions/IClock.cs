namespace UniversalErp.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
