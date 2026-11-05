using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Api.Security;

public sealed class RequestTenantContextAccessor : ITenantContextAccessor
{
    private TenantContext? current;

    public TenantContext Current => current
        ?? throw new InvalidOperationException("An authorized tenant context has not been resolved for this request.");

    public void SetCurrent(TenantContext tenantContext) => current = tenantContext;
}
