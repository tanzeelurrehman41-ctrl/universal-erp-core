using System.Security.Claims;
using System.Text.Json;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using UniversalErp.Api.Development;
using UniversalErp.Api.HealthChecks;
using UniversalErp.Api.Inventory;
using UniversalErp.Api.Finance;
using UniversalErp.Api.Purchasing;
using UniversalErp.Api.Sales;
using UniversalErp.Api.Security;
using UniversalErp.Api.Sync;
using UniversalErp.Api.Tenancy;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Finance;
using UniversalErp.Application.Foundation;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Purchasing;
using UniversalErp.Application.Sales;
using UniversalErp.Application.Sync;
using UniversalErp.Application.Tenancy;
using UniversalErp.Infrastructure.Finance;
using UniversalErp.Infrastructure.Foundation;
using UniversalErp.Infrastructure.Inventory;
using UniversalErp.Infrastructure.Purchasing;
using UniversalErp.Infrastructure.Sales;
using UniversalErp.Infrastructure.Sync;
using UniversalErp.Infrastructure.Tenancy;
using UniversalErp.Api.Foundation;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();
var databaseConnectionString = builder.Configuration.GetConnectionString("UniversalErp");

if (string.IsNullOrWhiteSpace(databaseConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:UniversalErp is required.");
}

builder.Services.AddSingleton(NpgsqlDataSource.Create(databaseConnectionString));
builder.Services.AddSingleton(new PostgresHealthCheck(databaseConnectionString));
var healthChecksBuilder = builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgresql");
builder.Services.AddProblemDetails();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            if (origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                if (uri.Host.EndsWith(".pages.dev", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith(".onrender.com", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            var customAllowed = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
            if (customAllowed != null && customAllowed.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});
builder.Services.AddScoped<IProductRepository, PostgresProductRepository>();
builder.Services.AddScoped<ProductCatalogService>();
builder.Services.AddScoped<IInventoryWorkflowRepository, PostgresInventoryWorkflowRepository>();
builder.Services.AddScoped<InventoryWorkflowService>();
builder.Services.AddScoped<ISyncChangeFeed, PostgresSyncChangeFeed>();
builder.Services.AddScoped<IOfflineReceiptSyncRepository, PostgresOfflineReceiptSyncRepository>();
builder.Services.AddScoped<IPurchasingRepository, PostgresPurchasingRepository>();
builder.Services.AddScoped<PurchasingService>();
builder.Services.AddScoped<ITenantOnboardingRepository, PostgresTenantOnboardingRepository>();
builder.Services.AddScoped<TenantOnboardingService>();
builder.Services.AddScoped<IFinanceRepository, PostgresFinanceRepository>();
builder.Services.AddScoped<FinanceService>();
builder.Services.AddScoped<ISupplyChainOutboxRepository, PostgresSupplyChainOutboxRepository>();
builder.Services.AddScoped<SupplyChainOutboxService>();
builder.Services.AddScoped<ISalesRepository, PostgresSalesRepository>();
builder.Services.AddScoped<SalesService>();
builder.Services.AddScoped<IFoundationRepository, PostgresFoundationRepository>();
builder.Services.AddScoped<FoundationService>();

var authProvider = builder.Configuration["Authentication:Provider"]?.Trim();
var isAuthExplicitlyConfigured = !string.IsNullOrWhiteSpace(authProvider);

if (isDevelopment && !isAuthExplicitlyConfigured)
{
    builder.Services.AddSingleton<ITenantContextAccessor, DevelopmentTenantContextAccessor>();
}
else
{
    var effectiveProvider = string.IsNullOrWhiteSpace(authProvider) ? "OIDC" : authProvider;

    if (effectiveProvider.Equals("Firebase", StringComparison.OrdinalIgnoreCase))
    {
        var configuredProjectId = builder.Configuration["Firebase:ProjectId"];
        var credentialJson = builder.Configuration["Firebase:CredentialJson"]
            ?? Environment.GetEnvironmentVariable("FIREBASE_CREDENTIAL_JSON");
        var credentialBase64 = builder.Configuration["Firebase:CredentialBase64"]
            ?? Environment.GetEnvironmentVariable("FIREBASE_CREDENTIAL_BASE64");
        var credentialPath = builder.Configuration["Firebase:CredentialPath"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")
            ?? @"C:\secrets\universal-erp-staging-firebase-adminsdk-fbsvc-286fd8c22d.json";

        if (FirebaseApp.DefaultInstance == null)
        {
            var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<Program>();
            GoogleCredential? cred = null;
            string? discoveredProjectId = null;
            string credentialSource = "None";

            if (!string.IsNullOrWhiteSpace(credentialJson))
            {
                try
                {
                    var trimmed = credentialJson.Trim();
                    using var doc = JsonDocument.Parse(trimmed);
                    if (doc.RootElement.TryGetProperty("project_id", out var pidProp))
                    {
                        discoveredProjectId = pidProp.GetString();
                    }
                    cred = GoogleCredential.FromJson(trimmed);
                    credentialSource = "Firebase:CredentialJson / FIREBASE_CREDENTIAL_JSON";
                }
                catch (Exception ex)
                {
                    startupLogger.LogWarning(ex, "Failed to parse Firebase credentials from JSON environment variable.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(credentialBase64))
            {
                try
                {
                    var raw = credentialBase64.Trim();
                    if (raw.StartsWith('\"') && raw.EndsWith('\"') && raw.Length >= 2)
                    {
                        raw = raw[1..^1].Trim();
                    }

                    string resolvedJson;
                    if (raw.StartsWith('{'))
                    {
                        resolvedJson = raw;
                    }
                    else
                    {
                        var decodedBytes = Convert.FromBase64String(raw);
                        resolvedJson = System.Text.Encoding.UTF8.GetString(decodedBytes);
                    }

                    using var doc = JsonDocument.Parse(resolvedJson);
                    if (doc.RootElement.TryGetProperty("project_id", out var pidProp))
                    {
                        discoveredProjectId = pidProp.GetString();
                    }
                    cred = GoogleCredential.FromJson(resolvedJson);
                    credentialSource = "Firebase:CredentialBase64 / FIREBASE_CREDENTIAL_BASE64";
                }
                catch (Exception ex)
                {
                    startupLogger.LogWarning(ex, "Failed to decode/parse Firebase credentials from Base64 environment variable.");
                }
            }
            else if (File.Exists(credentialPath))
            {
                try
                {
                    var fileContent = File.ReadAllText(credentialPath);
                    using var doc = JsonDocument.Parse(fileContent);
                    if (doc.RootElement.TryGetProperty("project_id", out var pidProp))
                    {
                        discoveredProjectId = pidProp.GetString();
                    }
                    cred = GoogleCredential.FromJson(fileContent);
                    credentialSource = $"File: {credentialPath}";
                }
                catch (Exception ex)
                {
                    startupLogger.LogWarning(ex, "Failed to parse Firebase credentials from file {Path}.", credentialPath);
                }
            }

            if (cred == null)
            {
                try
                {
                    cred = GoogleCredential.GetApplicationDefault();
                    credentialSource = "ApplicationDefaultCredentials";
                }
                catch
                {
                    // No ADC configured
                }
            }

            var finalProjectId = discoveredProjectId
                ?? (!string.IsNullOrWhiteSpace(configuredProjectId) ? configuredProjectId : "universal-erp-staging");

            if (cred != null)
            {
                NetworkSynchronizedClock.Initialize(startupLogger);
                NetworkSynchronizedClock.ApplyTo(cred, startupLogger);
                var appOptions = new AppOptions
                {
                    Credential = cred,
                    ProjectId = finalProjectId,
                };
                FirebaseApp.Create(appOptions);
                startupLogger.LogInformation("FirebaseApp initialized successfully. ProjectId: {ProjectId}, CredentialSource: {Source}",
                    finalProjectId, credentialSource);
            }
            else
            {
                startupLogger.LogWarning("Firebase credentials not configured at startup. Firebase Authentication requests will fail until valid credentials are provided.");
            }
        }

        healthChecksBuilder.AddCheck("firebase", () =>
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                return HealthCheckResult.Unhealthy("FirebaseApp has not been initialized. Check credentials.");
            }
            var pid = FirebaseApp.DefaultInstance.Options.ProjectId ?? "unspecified";
            return HealthCheckResult.Healthy($"FirebaseApp initialized for project '{pid}'");
        });

        builder.Services
            .AddAuthentication(FirebaseAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, FirebaseAuthenticationHandler>(
                FirebaseAuthenticationHandler.SchemeName, null);
    }
    else
    {
        var authority = builder.Configuration["Authentication:Authority"];
        var audience = builder.Configuration["Authentication:Audience"];
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
            authorityUri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException(
                "OIDC Authentication requires an HTTPS Authentication:Authority and an Authentication:Audience.");
        }

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = tokenContext =>
                    {
                        var issuer = tokenContext.SecurityToken.Issuer;
                        var identity = tokenContext.Principal?.Identities
                            .FirstOrDefault(candidate => candidate.IsAuthenticated);
                        var subject = tokenContext.Principal?.FindFirst("sub")?.Value;
                        if (string.IsNullOrWhiteSpace(issuer) || identity is null ||
                            string.IsNullOrWhiteSpace(subject))
                        {
                            tokenContext.Fail("The validated token must contain an issuer and subject.");
                            return Task.CompletedTask;
                        }

                        foreach (var existingIssuer in identity.FindAll("erp:issuer").ToArray())
                        {
                            identity.RemoveClaim(existingIssuer);
                        }
                        identity.AddClaim(new Claim("erp:issuer", issuer));
                        return Task.CompletedTask;
                    },
                };
            });
    }

    builder.Services.AddAuthorization(options =>
    {
        foreach (var permissionCode in new[]
        {
            "inventory.product.view", "inventory.product.create",
            "inventory.uom.view", "inventory.uom.create",
            "inventory.variant.view", "inventory.variant.create",
            "inventory.warehouse.view", "inventory.warehouse.create",
            "inventory.receipt.create", "inventory.receipt.post",
            "inventory.transfer.create", "inventory.adjustment.create",
            "inventory.availability.view",
            "purchasing.supplier.view", "purchasing.supplier.create",
            "purchasing.po.view", "purchasing.po.create", "purchasing.po.confirm",
            "purchasing.grn.view", "purchasing.grn.post",
            "sales.customer.view", "sales.customer.create",
            "sales.order.view", "sales.order.create", "sales.order.confirm",
            "sales.invoice.view", "sales.invoice.post",
        })
        {
            options.AddPolicy(permissionCode, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new DatabasePermissionRequirement(permissionCode)));
        }
    });
    builder.Services.AddScoped<RequestTenantContextAccessor>();
    builder.Services.AddScoped<ITenantContextAccessor>(serviceProvider =>
        serviceProvider.GetRequiredService<RequestTenantContextAccessor>());
    builder.Services.AddScoped<IAuthorizationHandler, DatabasePermissionAuthorizationHandler>();
    builder.Services.AddScoped<TenantCompanyContextCatalog>();
}

var app = builder.Build();
app.UseCors();

if (!isDevelopment || isAuthExplicitlyConfigured)
{
    app.UseAuthentication();
    app.UseMiddleware<AuthenticatedTenantContextMiddleware>();
    app.UseAuthorization();
}

var moduleCatalog = new[]
{
    new { id = "foundation", name = "Platform foundation", status = "In progress", description = "Tenant, company, location, identity, and database foundations." },
    new { id = "core", name = "Core application", status = "Starter", description = "API host and web, Windows, and Android application shells." },
    new { id = "inventory", name = "Inventory", status = "In progress", description = "Catalog, units, variants, warehouses, bins, and atomic untracked stock receipts; transfers, reservations, and valuation remain." },
    new { id = "purchasing", name = "Purchasing", status = "In progress", description = "Suppliers, purchase orders, goods received notes with idempotent posting and inventory outbox events; invoice matching and quality inspection remain." },
    new { id = "finance", name = "Finance", status = "In progress", description = "Chart of Accounts, balanced general ledger journals, and real-time trial balance." },
    new { id = "security", name = "Security and access", status = "In progress", description = "OIDC bearer validation and live membership/permission checks; user provisioning and client sign-in remain." },
    new { id = "offline", name = "Offline sync", status = "In progress", description = "Encrypted local journals, authenticated device grants, a durable receipt operation inbox, atomic snapshot cursors, and a paged change feed; event-level merge and product inbox processing remain." },
    new { id = "reporting", name = "Reporting", status = "Architecture only", description = "Approved datasets, dashboards, exports, and scheduled reports." },
    new { id = "saas", name = "SaaS platform", status = "In progress", description = "Tenant provisioning, plans, entitlements, subscriptions, and billing." },
};

app.UseExceptionHandler();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            entries = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    status = kvp.Value.Status.ToString(),
                    description = kvp.Value.Description
                })
        };
        await context.Response.WriteAsJsonAsync(payload);
    }
});
app.MapGet("/api/modules", () => Results.Ok(moduleCatalog));
app.MapSyncChangeEndpoints(isDevelopment);
app.MapOfflineReceiptSyncEndpoints(isDevelopment);

if (isDevelopment)
{
    app.MapPost("/api/v1/sync/operations:batch", async (
        ProductSyncBatchRequest batch,
        ProductCatalogService catalog,
        CancellationToken cancellationToken) =>
    {
        if (batch.BatchId == Guid.Empty || batch.Operations is null || batch.Operations.Count > 50)
        {
            return Results.BadRequest(new { code = "INVALID_SYNC_BATCH" });
        }

        var results = new List<ProductSyncOperationResult>(batch.Operations.Count);
        foreach (var operation in batch.Operations)
        {
            if (operation.OperationId == Guid.Empty ||
                operation.DeviceSequence <= 0 ||
                operation.OperationType != "product.create" ||
                operation.Payload is null)
            {
                results.Add(new ProductSyncOperationResult(
                    operation.OperationId,
                    operation.DeviceSequence,
                    "rejected",
                    null,
                    "INVALID_OPERATION"));
                continue;
            }

            var result = await catalog.CreateAsync(
                operation.Payload,
                operation.OperationId,
                cancellationToken);

            results.Add(result.IsSuccess
                ? new ProductSyncOperationResult(
                    operation.OperationId,
                    operation.DeviceSequence,
                    "accepted",
                    result.Value,
                    null)
                : new ProductSyncOperationResult(
                    operation.OperationId,
                    operation.DeviceSequence,
                    result.Error is "DUPLICATE_PRODUCT_CODE" or "IDEMPOTENCY_KEY_REUSED"
                        ? "conflict"
                        : "rejected",
                    null,
                    result.Error));
        }

        return Results.Ok(new ProductSyncBatchResponse(batch.BatchId, results));
    });
}

var productApi = app.MapGroup("/api/v1");
if (!isDevelopment)
{
    productApi.RequireAuthorization();
    productApi.MapGet("/contexts", async (
        ClaimsPrincipal principal,
        TenantCompanyContextCatalog catalog,
        CancellationToken cancellationToken) =>
    {
        var issuer = principal.FindFirst("erp:issuer")?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await catalog.ListForIdentityAsync(issuer, subject, cancellationToken));
    });
}

var listProducts = productApi.MapGet("/products", async (
    ProductCatalogService catalog,
    CancellationToken cancellationToken) =>
    Results.Ok(await catalog.ListAsync(cancellationToken)));
var getProduct = productApi.MapGet("/products/{productId:guid}", async (
    Guid productId,
    ProductCatalogService catalog,
    CancellationToken cancellationToken) =>
{
    var product = await catalog.GetByIdAsync(productId, cancellationToken);
    return product is null ? Results.NotFound() : Results.Ok(product);
});
var createProduct = productApi.MapPost("/products", async (
    HttpRequest httpRequest,
    CreateProductRequest request,
    ProductCatalogService catalog,
    CancellationToken cancellationToken) =>
{
    var keyHeader = httpRequest.Headers["Idempotency-Key"].ToString();
    Guid? idempotencyKey = Guid.TryParse(keyHeader, out var parsedKey)
        ? parsedKey
        : null;
    var result = await catalog.CreateAsync(request, idempotencyKey, cancellationToken);
    if (!result.IsSuccess)
    {
        var statusCode = result.Error is "DUPLICATE_PRODUCT_CODE" or "IDEMPOTENCY_KEY_REUSED"
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;
        return Results.Problem(
            statusCode: statusCode,
            title: result.Error,
            detail: result.Error switch
            {
                "DUPLICATE_PRODUCT_CODE" => "A product with this code already exists in the tenant.",
                "IDEMPOTENCY_KEY_REUSED" => "This Idempotency-Key was already used with a different request.",
                "INVALID_IDEMPOTENCY_KEY" => "Send a UUID in the Idempotency-Key header.",
                _ => "Check the product code, name, type, and description.",
            });
    }

    var product = result.Value!;
    return Results.Created($"/api/v1/products/{product.ProductId}", product);
});

if (!isDevelopment)
{
    listProducts.RequireAuthorization("inventory.product.view");
    getProduct.RequireAuthorization("inventory.product.view");
    createProduct.RequireAuthorization("inventory.product.create");
}

app.MapInventoryEndpoints(isDevelopment);
app.MapPurchasingEndpoints(isDevelopment);
app.MapSalesEndpoints(isDevelopment);
app.MapTenantEndpoints(isDevelopment);
app.MapFinanceEndpoints(isDevelopment);
productApi.MapFoundationEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    name = "Universal ERP API",
    status = "starter",
    message = "The authenticated API foundation is running. Business modules are being integrated.",
}));

app.Run();

public partial class Program { }
