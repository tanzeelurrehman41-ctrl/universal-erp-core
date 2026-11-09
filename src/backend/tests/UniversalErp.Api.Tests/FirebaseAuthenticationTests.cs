using System;
using System.IO;
using System.Text;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalErp.Api.Security;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class FirebaseAuthenticationTests
{
    [Fact]
    public void Base64_ServiceAccount_Decodes_And_Initializes_FirebaseApp()
    {
        var secretPath = @"C:\secrets\universal-erp-staging-firebase-adminsdk-fbsvc-286fd8c22d.json";
        if (!File.Exists(secretPath))
        {
            // Skip if secrets file is not present locally
            return;
        }

        var json = File.ReadAllText(secretPath);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        // Test in-memory decoding
        var decodedBytes = Convert.FromBase64String(base64);
        var decodedJson = Encoding.UTF8.GetString(decodedBytes);

        using var doc = JsonDocument.Parse(decodedJson);
        var projectId = doc.RootElement.GetProperty("project_id").GetString();
        Assert.Equal("universal-erp-staging", projectId);

        var cred = GoogleCredential.FromJson(decodedJson);
        Assert.NotNull(cred);

        if (FirebaseApp.DefaultInstance == null)
        {
            FirebaseApp.Create(new AppOptions
            {
                Credential = cred,
                ProjectId = projectId
            });
        }

        Assert.NotNull(FirebaseApp.DefaultInstance);
        var auth = FirebaseAuth.DefaultInstance;
        Assert.NotNull(auth);

        // Test clock synchronization reflection
        NetworkSynchronizedClock.Initialize(NullLogger.Instance);
        NetworkSynchronizedClock.ApplyTo(auth, NullLogger.Instance);
    }

    [Fact]
    public void Base64_With_Wrapping_Quotes_And_Whitespace_Is_Cleaned_And_Decoded()
    {
        var secretPath = @"C:\secrets\universal-erp-staging-firebase-adminsdk-fbsvc-286fd8c22d.json";
        if (!File.Exists(secretPath))
        {
            return;
        }

        var json = File.ReadAllText(secretPath);
        var rawBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var quoteWrappedWithWhitespace = $"  \"{rawBase64}\"  \r\n";

        // Simulate Program.cs cleaning logic
        var raw = quoteWrappedWithWhitespace.Trim();
        if (raw.StartsWith('\"') && raw.EndsWith('\"') && raw.Length >= 2)
        {
            raw = raw[1..^1].Trim();
        }

        var decodedBytes = Convert.FromBase64String(raw);
        var resolvedJson = Encoding.UTF8.GetString(decodedBytes);

        using var doc = JsonDocument.Parse(resolvedJson);
        Assert.True(doc.RootElement.TryGetProperty("project_id", out var pidProp));
        Assert.Equal("universal-erp-staging", pidProp.GetString());
    }

    [Fact]
    public void Raw_Json_In_Base64_Variable_Is_Parsed_Directly()
    {
        var secretPath = @"C:\secrets\universal-erp-staging-firebase-adminsdk-fbsvc-286fd8c22d.json";
        if (!File.Exists(secretPath))
        {
            return;
        }

        var json = File.ReadAllText(secretPath);
        var rawPastedJson = $"  {json}  \r\n";

        var raw = rawPastedJson.Trim();
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
            resolvedJson = Encoding.UTF8.GetString(decodedBytes);
        }

        using var doc = JsonDocument.Parse(resolvedJson);
        Assert.True(doc.RootElement.TryGetProperty("project_id", out var pidProp));
        Assert.Equal("universal-erp-staging", pidProp.GetString());
    }

    [Fact]
    public async Task VerifyIdTokenAsync_WithInvalidToken_ThrowsFirebaseAuthException()
    {
        var secretPath = @"C:\secrets\universal-erp-staging-firebase-adminsdk-fbsvc-286fd8c22d.json";
        if (!File.Exists(secretPath))
        {
            return;
        }

        var json = File.ReadAllText(secretPath);
        var cred = GoogleCredential.FromJson(json);

        if (FirebaseApp.DefaultInstance == null)
        {
            FirebaseApp.Create(new AppOptions
            {
                Credential = cred,
                ProjectId = "universal-erp-staging"
            });
        }

        var auth = FirebaseAuth.DefaultInstance;
        Assert.NotNull(auth);

        var ex = await Assert.ThrowsAsync<FirebaseAuthException>(
            () => auth.VerifyIdTokenAsync("invalid-dummy-token", checkRevoked: false));

        Assert.NotNull(ex.Message);
    }
}
