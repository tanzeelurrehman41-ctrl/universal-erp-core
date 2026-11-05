namespace UniversalErp.Domain.Tenancy;

/// <summary>
/// Represents trusted tenant scope resolved by server-side authentication.
/// Never construct this directly from an untrusted request body.
/// </summary>
public sealed record TenantContext(Guid TenantId, Guid CompanyId, Guid UserId);
