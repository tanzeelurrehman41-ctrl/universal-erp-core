using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UniversalErp.Application.Security.AccessControl;
using UniversalErp.Application.Security.ApiSecurity;
using UniversalErp.Application.Security.AuditLogging;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class EnterpriseSecurityTests
{
    [Fact]
    public void AuditLogChain_ValidSequence_PassesCryptographicVerification()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var events = new List<SecurityAuditEvent>();

        // Event 1
        var ev1 = AuditLogChainService.CreateEvent(
            prevHash: AuditLogChainService.GenesisHash,
            tenantId: tenantId,
            companyId: companyId,
            branchId: branchId,
            actorId: actorId,
            actorUsername: "controller_admin",
            action: "JOURNAL_POST",
            entityType: "GlJournal",
            entityId: "JRN-2026-001",
            clientIpAddress: "127.0.0.1",
            beforeState: null,
            afterState: new { Amount = 150000m, Currency = "PKR" },
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-5));
        events.Add(ev1);

        // Event 2 chained to Event 1
        var ev2 = AuditLogChainService.CreateEvent(
            prevHash: ev1.RecordHash,
            tenantId: tenantId,
            companyId: companyId,
            branchId: branchId,
            actorId: actorId,
            actorUsername: "controller_admin",
            action: "PAYROLL_RUN",
            entityType: "PayrollBatch",
            entityId: "PAY-2026-05",
            clientIpAddress: "127.0.0.1",
            beforeState: null,
            afterState: new { TotalGross = 250000m, Currency = "USD" },
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-4));
        events.Add(ev2);

        // Event 3 chained to Event 2
        var ev3 = AuditLogChainService.CreateEvent(
            prevHash: ev2.RecordHash,
            tenantId: tenantId,
            companyId: companyId,
            branchId: branchId,
            actorId: actorId,
            actorUsername: "controller_admin",
            action: "TAX_DETERMINE",
            entityType: "TaxDetermination",
            entityId: "TAX-2026-99",
            clientIpAddress: "127.0.0.1",
            beforeState: null,
            afterState: new { TaxRate = 15.0m, Regime = "SRB" },
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-3));
        events.Add(ev3);

        var result = AuditLogChainService.VerifyChain(events);
        Assert.True(result.IsChainValid);
        Assert.Equal(3, result.TotalEventsChecked);
        Assert.Equal(-1, result.TamperedIndex);
    }

    [Fact]
    public void AuditLogChain_TamperedPayload_FailsVerificationAndIdentifiesTamperedIndex()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var ev1 = AuditLogChainService.CreateEvent(
            prevHash: AuditLogChainService.GenesisHash,
            tenantId: tenantId,
            companyId: companyId,
            branchId: null,
            actorId: actorId,
            actorUsername: "user1",
            action: "CREATE",
            entityType: "Invoice",
            entityId: "INV-001",
            clientIpAddress: "10.0.0.1",
            beforeState: null,
            afterState: new { Total = 1000m });

        var ev2 = AuditLogChainService.CreateEvent(
            prevHash: ev1.RecordHash,
            tenantId: tenantId,
            companyId: companyId,
            branchId: null,
            actorId: actorId,
            actorUsername: "user1",
            action: "APPROVE",
            entityType: "Invoice",
            entityId: "INV-001",
            clientIpAddress: "10.0.0.1",
            beforeState: new { Total = 1000m },
            afterState: new { Total = 1000m, Status = "Approved" });

        // Maliciously alter AfterStateJson in ev2 without updating hash
        var tamperedEv2 = ev2 with { AfterStateJson = "{\"Total\":9999999,\"Status\":\"Tampered\"}" };

        var events = new List<SecurityAuditEvent> { ev1, tamperedEv2 };
        var result = AuditLogChainService.VerifyChain(events);

        Assert.False(result.IsChainValid);
        Assert.Equal(1, result.TamperedIndex);
        Assert.Equal(ev2.EventId.ToString(), result.TamperedEventId);
        Assert.Contains("Tamper detected", result.FailureReason);
    }

    [Fact]
    public void Rbac_TenantAndCompanyIsolation_EnforcesLeastPrivilege()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var company1 = Guid.NewGuid();
        var company2 = Guid.NewGuid();

        var userTenantA = new UserSecurityContext(
            UserId: Guid.NewGuid(),
            Username: "alice",
            TenantId: tenantA,
            CompanyId: company1,
            BranchId: null,
            Role: "CompanyAdmin",
            Permissions: new HashSet<string>());

        // 1. Cross-tenant access is blocked
        Assert.False(TenantScopedRbacService.EvaluateAccess(userTenantA, targetTenantId: tenantB, targetCompanyId: company1, targetBranchId: null, "finance:post"));

        // 2. Cross-company access for standard CompanyAdmin is blocked
        Assert.False(TenantScopedRbacService.EvaluateAccess(userTenantA, targetTenantId: tenantA, targetCompanyId: company2, targetBranchId: null, "finance:post"));

        // 3. Authorized company access succeeds
        Assert.True(TenantScopedRbacService.EvaluateAccess(userTenantA, targetTenantId: tenantA, targetCompanyId: company1, targetBranchId: null, "finance:post"));

        // 4. Privileged support access session allows scoped access during valid time window
        var supportUser = new UserSecurityContext(
            UserId: Guid.NewGuid(),
            Username: "support_ops",
            TenantId: tenantA,
            CompanyId: null,
            BranchId: null,
            Role: "SupportSpecialist",
            Permissions: new HashSet<string>(),
            IsPrivilegedSupport: true);

        Assert.True(TenantScopedRbacService.EvaluateAccess(supportUser, targetTenantId: tenantA, targetCompanyId: company1, targetBranchId: null, "finance:read"));
    }

    [Fact]
    public void DataMasking_ObfuscatesSensitivePiiAccurately()
    {
        // US SSN
        Assert.Equal("***-**-6789", DataMaskingService.MaskSsn("123-45-6789"));
        Assert.Equal("***-**-6789", DataMaskingService.MaskSsn("123456789"));

        // Pakistani CNIC
        Assert.Equal("42101-*******-1", DataMaskingService.MaskCnic("42101-1234567-1"));
        Assert.Equal("42101-*******-1", DataMaskingService.MaskCnic("4210112345671"));

        // Bank Account
        Assert.Equal("********4321", DataMaskingService.MaskBankAccount("123456784321"));

        // Credit Card
        Assert.Equal("**** **** **** 1111", DataMaskingService.MaskCreditCard("4111222233331111"));

        // Email
        Assert.Equal("j***e@universal-erp.com", DataMaskingService.MaskEmail("jane@universal-erp.com"));
    }

    [Fact]
    public void TokenBucketRateLimiter_EnforcesPerTenantLimitsAndRefills()
    {
        var limiter = new TokenBucketRateLimiter(capacity: 3, refillPerSecond: 100);

        // 3 requests allowed
        Assert.True(limiter.CheckLimit("tenant_123").IsAllowed);
        Assert.True(limiter.CheckLimit("tenant_123").IsAllowed);
        Assert.True(limiter.CheckLimit("tenant_123").IsAllowed);

        // 4th request blocked immediately
        var blocked = limiter.CheckLimit("tenant_123");
        Assert.False(blocked.IsAllowed);
        Assert.True(blocked.RetryAfter > TimeSpan.Zero);

        // Distinct tenant is unaffected
        Assert.True(limiter.CheckLimit("tenant_456").IsAllowed);

        // Wait for refill
        Thread.Sleep(50);
        Assert.True(limiter.CheckLimit("tenant_123").IsAllowed);
    }

    [Fact]
    public void WebhookSecurity_AesGcmEncryption_AndHmacSignature_RoundTrip()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var payload = "{\"event\":\"invoice.paid\",\"amount\":1500.00,\"currency\":\"USD\"}";
        var secret = "whsec_super_secret_signing_key_2026!";

        // 1. AES-256-GCM Encryption & Decryption
        var encrypted = WebhookSecurityService.EncryptAesGcm(payload, key);
        Assert.NotEqual(payload, encrypted);

        var decrypted = WebhookSecurityService.DecryptAesGcm(encrypted, key);
        Assert.Equal(payload, decrypted);

        // 2. HMAC-SHA256 Signature Generation & Verification
        var signature = WebhookSecurityService.GenerateHmacSignature(payload, secret);
        Assert.True(WebhookSecurityService.VerifyHmacSignature(payload, signature, secret));
        Assert.False(WebhookSecurityService.VerifyHmacSignature(payload, "invalid_sig", secret));
        Assert.False(WebhookSecurityService.VerifyHmacSignature("tampered_payload", signature, secret));

        // 3. Replay Protection
        var webhookSec = new WebhookSecurityService(TimeSpan.FromMinutes(5));
        var nonce = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        Assert.True(webhookSec.ValidateReplayAndTimestamp(nonce, now));
        // Replaying same nonce must be rejected
        Assert.False(webhookSec.ValidateReplayAndTimestamp(nonce, now));
        // Expired timestamp (>5 min) must be rejected
        Assert.False(webhookSec.ValidateReplayAndTimestamp(Guid.NewGuid().ToString("N"), now.AddMinutes(-10)));
    }
}
