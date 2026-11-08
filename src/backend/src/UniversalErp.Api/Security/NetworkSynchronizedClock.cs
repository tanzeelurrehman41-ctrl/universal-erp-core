using System.Reflection;
using FirebaseAdmin.Auth;
using Google.Apis.Util;
using Microsoft.Extensions.Logging;

namespace UniversalErp.Api.Security;

public sealed class NetworkSynchronizedClock : IClock
{
    private static TimeSpan _driftOffset = TimeSpan.Zero;
    private static bool _initialized = false;
    private static bool _applied = false;
    private static readonly object _lock = new();

    public static TimeSpan DriftOffset => _driftOffset;

    public static void Initialize(ILogger logger)
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var response = client.Send(new HttpRequestMessage(HttpMethod.Head, "https://www.google.com"));
                if (response.Headers.Date.HasValue)
                {
                    var googleUtc = response.Headers.Date.Value.UtcDateTime;
                    var localUtc = DateTime.UtcNow;
                    _driftOffset = googleUtc - localUtc;
                    logger.LogInformation(
                        "Synchronized clock drift against Google servers. Local: {Local}, Google: {Google}, Offset: {Offset}",
                        localUtc, googleUtc, _driftOffset);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not contact Google for clock drift check; using local clock.");
            }
            _initialized = true;
        }
    }

    public DateTime Now => UtcNow.ToLocalTime();
    public DateTime UtcNow => DateTime.UtcNow + _driftOffset;

    public static void ApplyTo(Google.Apis.Auth.OAuth2.GoogleCredential credential, ILogger logger)
    {
        try
        {
            var underlying = credential.UnderlyingCredential;
            if (underlying is null) return;
            var type = underlying.GetType();
            while (type is not null)
            {
                var field = type.GetField("<Clock>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field is not null)
                {
                    field.SetValue(underlying, new NetworkSynchronizedClock());
                    logger.LogInformation("Successfully configured NetworkSynchronizedClock on ServiceCredential.");
                    return;
                }
                type = type.BaseType;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply NetworkSynchronizedClock to ServiceCredential.");
        }
    }

    public static void ApplyTo(FirebaseAuth authInstance, ILogger logger)
    {
        if (_applied) return;
        lock (_lock)
        {
            if (_applied) return;
            try
            {
                var baseType = typeof(FirebaseAuth).BaseType;
                var idTokenVerifierLazyField = baseType?.GetField("idTokenVerifier", BindingFlags.NonPublic | BindingFlags.Instance);
                var lazyVerifier = idTokenVerifierLazyField?.GetValue(authInstance);
                if (lazyVerifier != null)
                {
                    var valueProperty = lazyVerifier.GetType().GetProperty("Value");
                    var verifierInstance = valueProperty?.GetValue(lazyVerifier);
                    if (verifierInstance != null)
                    {
                        var clockField = verifierInstance.GetType().GetField("clock", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (clockField != null)
                        {
                            clockField.SetValue(verifierInstance, new NetworkSynchronizedClock());
                            logger.LogInformation("Successfully configured NetworkSynchronizedClock on FirebaseTokenVerifier.");
                            _applied = true;
                            return;
                        }
                    }
                }
                logger.LogWarning("Could not access idTokenVerifier field on FirebaseAuth instance.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to apply NetworkSynchronizedClock to FirebaseAuth.");
            }
        }
    }
}
