using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Inventory;
using UniversalErp.Infrastructure.Sync;

namespace UniversalErp.Infrastructure.Inventory;

public sealed class PostgresProductRepository(NpgsqlDataSource dataSource) : IProductRepository
{
    private const string ProductColumns = """
        tenant_id, product_id, product_code, name, description,
        product_type, is_active, created_at, updated_at
        """;

    public async Task<IReadOnlyList<Product>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenantId, null, cancellationToken);

        await using var command = new NpgsqlCommand(
            $"SELECT {ProductColumns} FROM erp_inventory.product WHERE tenant_id = @tenantId ORDER BY product_code",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);

        var products = new List<Product>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(ReadProduct(reader));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return products;
    }

    public async Task<Product?> GetByIdAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenantId, null, cancellationToken);

        await using var command = new NpgsqlCommand(
            $"SELECT {ProductColumns} FROM erp_inventory.product WHERE tenant_id = @tenantId AND product_id = @productId",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("productId", productId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var product = await reader.ReadAsync(cancellationToken) ? ReadProduct(reader) : null;
        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return product;
    }

    public async Task<Product> CreateAsync(
        Guid tenantId,
        Guid companyId,
        Guid idempotencyKey,
        string requestHash,
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        await using (var reserveKey = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.command_idempotency
                (tenant_id, idempotency_key, command_code, request_hash)
            VALUES (@tenantId, @idempotencyKey, 'product.create', @requestHash)
            ON CONFLICT (tenant_id, idempotency_key) DO NOTHING
            RETURNING idempotency_key
            """,
            connection,
            transaction))
        {
            reserveKey.Parameters.AddWithValue("tenantId", tenantId);
            reserveKey.Parameters.AddWithValue("idempotencyKey", idempotencyKey);
            reserveKey.Parameters.AddWithValue("requestHash", requestHash);

            var reservedKey = await reserveKey.ExecuteScalarAsync(cancellationToken);
            if (reservedKey is null or DBNull)
            {
                return await ReplayExistingAsync(
                    connection,
                    transaction,
                    tenantId,
                    idempotencyKey,
                    requestHash,
                    cancellationToken);
            }
        }

        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO erp_inventory.product
                (tenant_id, product_id, product_code, name, description, product_type)
            VALUES
                (@tenantId, @productId, @productCode, @name, @description, @productType)
            RETURNING {ProductColumns}
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("productId", Guid.NewGuid());
        command.Parameters.AddWithValue("productCode", request.ProductCode!);
        command.Parameters.AddWithValue("name", request.Name!);
        command.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("productType", request.ProductType!);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("PostgreSQL did not return the created product.");
            }

            var product = ReadProduct(reader);
            await reader.CloseAsync();

            var changePayload = JsonSerializer.Serialize(new
            {
                tenantId = product.TenantId,
                productId = product.ProductId,
                productCode = product.ProductCode,
                name = product.Name,
                description = product.Description,
                productType = product.ProductType,
                isActive = product.IsActive,
                createdAt = product.CreatedAt,
                updatedAt = product.UpdatedAt,
            });
            await CompanyChangeLogWriter.AppendTenantWideAsync(
                connection,
                transaction,
                tenantId,
                companyId,
                "product",
                product.ProductId,
                1,
                "upsert",
                changePayload,
                cancellationToken);

            await using var saveResult = new NpgsqlCommand(
                "UPDATE erp_inventory.command_idempotency SET resource_id = @productId WHERE tenant_id = @tenantId AND idempotency_key = @idempotencyKey",
                connection,
                transaction);
            saveResult.Parameters.AddWithValue("tenantId", tenantId);
            saveResult.Parameters.AddWithValue("idempotencyKey", idempotencyKey);
            saveResult.Parameters.AddWithValue("productId", product.ProductId);
            await saveResult.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return product;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ProductCodeConflictException();
        }
    }

    private static async Task<Product> ReplayExistingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        await using var findKey = new NpgsqlCommand(
            "SELECT command_code, request_hash, resource_id FROM erp_inventory.command_idempotency WHERE tenant_id = @tenantId AND idempotency_key = @idempotencyKey FOR UPDATE",
            connection,
            transaction);
        findKey.Parameters.AddWithValue("tenantId", tenantId);
        findKey.Parameters.AddWithValue("idempotencyKey", idempotencyKey);

        await using var reader = await findKey.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The idempotency record disappeared during the transaction.");
        }

        var commandCode = reader.GetString(0);
        var savedHash = reader.GetString(1).Trim();
        var resourceId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
        await reader.CloseAsync();

        if (commandCode != "product.create" || !string.Equals(savedHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyKeyConflictException();
        }
        if (resourceId is null)
        {
            throw new InvalidOperationException("The original product command has no result.");
        }

        await using var findProduct = new NpgsqlCommand(
            $"SELECT {ProductColumns} FROM erp_inventory.product WHERE tenant_id = @tenantId AND product_id = @productId",
            connection,
            transaction);
        findProduct.Parameters.AddWithValue("tenantId", tenantId);
        findProduct.Parameters.AddWithValue("productId", resourceId.Value);

        await using var productReader = await findProduct.ExecuteReaderAsync(cancellationToken);
        if (!await productReader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The product created by the original command no longer exists.");
        }

        var product = ReadProduct(productReader);
        await productReader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return product;
    }

    private static async Task SetTenantScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true)",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId.ToString());
        command.Parameters.AddWithValue("companyId", companyId?.ToString() ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Product ReadProduct(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetString(5),
        reader.GetBoolean(6),
        reader.GetFieldValue<DateTimeOffset>(7),
        reader.GetFieldValue<DateTimeOffset>(8));
}
