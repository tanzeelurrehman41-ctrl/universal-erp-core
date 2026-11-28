using System.Text.RegularExpressions;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Sync;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
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

    Task<ProductSyncOperationResult> CreateOfflineAsync(
        TenantContext context,
        ProductSyncBatchRequest batch,
        ProductSyncOperation operation,
        CreateProductRequest? normalizedRequest,
        string requestHash,
        string? validationError,
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

    public async Task<ProductSyncOperationResult> CreateOfflineAsync(
        ProductSyncBatchRequest batch,
        ProductSyncOperation operation,
        CancellationToken cancellationToken)
    {
        var request = operation.Payload;
        string? validationError = null;
        CreateProductRequest? normalizedRequest = null;

        var productCode = request?.ProductCode?.Trim().ToUpperInvariant();
        var name = request?.Name?.Trim();
        var productType = request?.ProductType?.Trim().ToLowerInvariant();
        var description = string.IsNullOrWhiteSpace(request?.Description)
            ? null
            : request.Description.Trim();

        if (!string.Equals(operation.OperationType, "product.create", StringComparison.Ordinal))
        {
            validationError = "INVALID_PRODUCT_SYNC_OPERATION";
        }
        else if (request is null)
        {
            validationError = "INVALID_PRODUCT_PAYLOAD";
        }
        else if (string.IsNullOrEmpty(productCode) || !ProductCodePattern.IsMatch(productCode))
        {
            validationError = "INVALID_PRODUCT_CODE";
        }
        else if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            validationError = "INVALID_PRODUCT_NAME";
        }
        else if (productType is null || !ProductTypes.Contains(productType))
        {
            validationError = "INVALID_PRODUCT_TYPE";
        }
        else if (description?.Length > 2000)
        {
            validationError = "DESCRIPTION_TOO_LONG";
        }
        else
        {
            normalizedRequest = new CreateProductRequest(productCode, name, description, productType);
        }

        var canonicalPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            command = "product.create",
            productCode = normalizedRequest?.ProductCode ?? request?.ProductCode,
            name = normalizedRequest?.Name ?? request?.Name,
            description = normalizedRequest?.Description ?? request?.Description,
            productType = normalizedRequest?.ProductType ?? request?.ProductType,
        });
        var requestHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalPayload)));

        return await productRepository.CreateOfflineAsync(
            tenantContextAccessor.Current,
            batch,
            operation,
            normalizedRequest,
            requestHash,
            validationError,
            cancellationToken);
    }

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
