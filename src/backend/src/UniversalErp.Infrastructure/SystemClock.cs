using UniversalErp.Application.Abstractions;

namespace UniversalErp.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
