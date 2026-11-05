using System.Text.RegularExpressions;
using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Inventory;

namespace UniversalErp.Application.Inventory;

public sealed record CreateProductRequest(
    string? ProductCode,
    string? Name,
    string? Description,
    string? ProductType);

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Product?> GetByIdAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken);
    Task<Product> CreateAsync(
        Guid tenantId,
        Guid companyId,
        Guid idempotencyKey,
        string requestHash,
        CreateProductRequest request,
        CancellationToken cancellationToken);
}

public sealed class ProductCodeConflictException : Exception
{
}

public sealed class IdempotencyKeyConflictException : Exception
{
}

public sealed class ProductCatalogService(
    ITenantContextAccessor tenantContextAccessor,
    IProductRepository productRepository)
{
    private static readonly Regex ProductCodePattern = new(
        @"\A[A-Z0-9][A-Z0-9._/-]{0,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> ProductTypes = new(StringComparer.Ordinal)
    {
        "stock",
        "service",
        "non_stock",
    };

    public Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken) =>
        productRepository.ListAsync(tenantContextAccessor.Current.TenantId, cancellationToken);

    public Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken) =>
        productRepository.GetByIdAsync(
            tenantContextAccessor.Current.TenantId,
            productId,
            cancellationToken);

    public async Task<Result<Product>> CreateAsync(
        CreateProductRequest request,
        Guid? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<Product>.Failure("INVALID_IDEMPOTENCY_KEY");
        }

        var productCode = request.ProductCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(productCode) || !ProductCodePattern.IsMatch(productCode))
        {
            return Result<Product>.Failure("INVALID_PRODUCT_CODE");
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return Result<Product>.Failure("INVALID_PRODUCT_NAME");
        }

        var productType = request.ProductType?.Trim().ToLowerInvariant();
        if (productType is null || !ProductTypes.Contains(productType))
        {
            return Result<Product>.Failure("INVALID_PRODUCT_TYPE");
        }

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        if (description?.Length > 2000)
        {
            return Result<Product>.Failure("DESCRIPTION_TOO_LONG");
        }

        var normalizedRequest = new CreateProductRequest(
            productCode,
            name,
            description,
            productType);
        var canonicalPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            command = "product.create",
            productCode,
            name,
            description,
            productType,
        });
        var requestHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalPayload)));

        try
        {
            var product = await productRepository.CreateAsync(
                tenantContextAccessor.Current.TenantId,
                tenantContextAccessor.Current.CompanyId,
                idempotencyKey.Value,
                requestHash,
                normalizedRequest,
                cancellationToken);
            return Result<Product>.Success(product);
        }
        catch (ProductCodeConflictException)
        {
            return Result<Product>.Failure("DUPLICATE_PRODUCT_CODE");
        }
        catch (IdempotencyKeyConflictException)
        {
            return Result<Product>.Failure("IDEMPOTENCY_KEY_REUSED");
        }
    }
}
