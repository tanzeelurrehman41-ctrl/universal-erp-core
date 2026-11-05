using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Application.Abstractions;

public interface ITenantContextAccessor
{
    TenantContext Current { get; }
}
