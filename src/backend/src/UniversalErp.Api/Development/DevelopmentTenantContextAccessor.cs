using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Api.Development;

public sealed class DevelopmentTenantContextAccessor : ITenantContextAccessor
{
    public DevelopmentTenantContextAccessor(IConfiguration configuration)
    {
        var settings = configuration.GetSection("DevelopmentTenant");
        Current = new TenantContext(
            Guid.Parse(settings["TenantId"] ?? throw new InvalidOperationException("Development tenant ID is missing.")),
            Guid.Parse(settings["CompanyId"] ?? throw new InvalidOperationException("Development company ID is missing.")),
            Guid.Parse(settings["UserId"] ?? throw new InvalidOperationException("Development user ID is missing.")));
    }

    public TenantContext Current { get; }
}
