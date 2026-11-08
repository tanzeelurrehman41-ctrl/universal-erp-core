using System;

namespace UniversalErp.Domain.Inventory;

public sealed record Product(
    Guid TenantId,
    Guid ProductId,
    string ProductCode,
    string Name,
    string? Description,
    string ProductType,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
