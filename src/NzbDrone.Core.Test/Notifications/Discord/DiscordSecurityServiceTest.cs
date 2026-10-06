using System;
using System.Text;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Discord;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace NzbDrone.Core.Test.Notifications.Discord;

[TestFixture]
public class DiscordSecurityServiceTest
{
    private DiscordSecurityService _service;
    private string _privateKeyHex;
    private string _publicKeyHex;

    [SetUp]
    public void SetUp()
    {
        _service = new DiscordSecurityService();

        var keyGen = new Ed25519KeyPairGenerator();
        keyGen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = keyGen.GenerateKeyPair();

        var privKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        var pubKey = (Ed25519PublicKeyParameters)keyPair.Public;

        _privateKeyHex = Convert.ToHexString(privKey.GetEncoded()).ToLowerInvariant();
        _publicKeyHex = Convert.ToHexString(pubKey.GetEncoded()).ToLowerInvariant();
    }

    [Test]
    public void VerifySignature_should_return_true_for_valid_ed25519_signature()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, timestamp, bodyBytes);

        var isValid = _service.VerifySignature(signatureHex, timestamp, bodyBytes, _publicKeyHex);

        Assert.That(isValid, Is.True);
    }

    [Test]
    public void VerifySignature_should_return_false_when_body_is_modified()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, timestamp, bodyBytes);

        var tamperedBodyBytes = Encoding.UTF8.GetBytes("{\"type\":2}");
        var isValid = _service.VerifySignature(signatureHex, timestamp, tamperedBodyBytes, _publicKeyHex);

        Assert.That(isValid, Is.False);
    }

    [Test]
    public void VerifySignature_should_return_false_when_signature_is_invalid()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var invalidSignatureHex = new string('0', 128);

        var isValid = _service.VerifySignature(invalidSignatureHex, timestamp, bodyBytes, _publicKeyHex);

        Assert.That(isValid, Is.False);
    }

    [Test]
    public void VerifySignature_should_return_false_when_timestamp_is_older_than_5_minutes()
    {
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, oldTimestamp, bodyBytes);

        var isValid = _service.VerifySignature(signatureHex, oldTimestamp, bodyBytes, _publicKeyHex);

        Assert.That(isValid, Is.False);
    }

    [Test]
    public void VerifySignature_should_return_false_when_timestamp_is_in_future_by_more_than_5_minutes()
    {
        var futureTimestamp = DateTimeOffset.UtcNow.AddMinutes(6).ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, futureTimestamp, bodyBytes);

        var isValid = _service.VerifySignature(signatureHex, futureTimestamp, bodyBytes, _publicKeyHex);

        Assert.That(isValid, Is.False);
    }

    [Test]
    public void VerifySignature_should_return_false_when_parameters_are_null_or_empty()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, timestamp, bodyBytes);

        Assert.That(_service.VerifySignature(null, timestamp, bodyBytes, _publicKeyHex), Is.False);
        Assert.That(_service.VerifySignature(signatureHex, null, bodyBytes, _publicKeyHex), Is.False);
        Assert.That(_service.VerifySignature(signatureHex, timestamp, bodyBytes, null), Is.False);
        Assert.That(_service.VerifySignature("   ", timestamp, bodyBytes, _publicKeyHex), Is.False);
    }

    [Test]
    public void VerifySignature_should_return_false_when_hex_is_malformed()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");

        Assert.That(_service.VerifySignature("not_hex", timestamp, bodyBytes, _publicKeyHex), Is.False);
        Assert.That(_service.VerifySignature(new string('a', 64), timestamp, bodyBytes, _publicKeyHex), Is.False); // Wrong length (must be 128 hex chars)
        Assert.That(_service.VerifySignature(new string('a', 128), timestamp, bodyBytes, "short_key"), Is.False);
    }

    [Test]
    public void VerifySignature_should_support_iso8601_timestamp()
    {
        var isoTimestamp = DateTimeOffset.UtcNow.ToString("o");
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, isoTimestamp, bodyBytes);

        var isValid = _service.VerifySignature(signatureHex, isoTimestamp, bodyBytes, _publicKeyHex);

        Assert.That(isValid, Is.True);
    }

    [Test]
    public void VerifySignature_should_return_false_when_signature_is_replayed()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, timestamp, bodyBytes);

        var firstResult = _service.VerifySignature(signatureHex, timestamp, bodyBytes, _publicKeyHex);
        var replayResult = _service.VerifySignature(signatureHex, timestamp, bodyBytes, _publicKeyHex);

        Assert.That(firstResult, Is.True);
        Assert.That(replayResult, Is.False);
    }

    [Test]
    public void VerifySignature_should_return_false_when_replayed_with_different_casing()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, timestamp, bodyBytes);

        var firstResult = _service.VerifySignature(signatureHex.ToLowerInvariant(), timestamp, bodyBytes, _publicKeyHex);
        var replayResult = _service.VerifySignature(signatureHex.ToUpperInvariant(), timestamp, bodyBytes, _publicKeyHex);

        Assert.That(firstResult, Is.True);
        Assert.That(replayResult, Is.False);
    }

    [Test]
    public void VerifySignature_should_allow_signature_again_after_cache_cleared()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, timestamp, bodyBytes);

        var firstResult = _service.VerifySignature(signatureHex, timestamp, bodyBytes, _publicKeyHex);
        Assert.That(firstResult, Is.True);
        Assert.That(_service.ReplayCacheCount, Is.EqualTo(1));

        _service.ClearReplayCache();
        Assert.That(_service.ReplayCacheCount, Is.EqualTo(0));

        var secondResult = _service.VerifySignature(signatureHex, timestamp, bodyBytes, _publicKeyHex);
        Assert.That(secondResult, Is.True);
    }

    [Test]
    public void VerifySignature_should_return_false_when_timestamp_is_in_future_beyond_clock_skew()
    {
        var futureTimestamp = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, futureTimestamp, bodyBytes);

        var isValid = _service.VerifySignature(signatureHex, futureTimestamp, bodyBytes, _publicKeyHex);

        Assert.That(isValid, Is.False);
    }

    [Test]
    public void VerifySignature_should_return_true_when_timestamp_is_in_future_within_clock_skew()
    {
        var futureTimestamp = DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeSeconds().ToString();
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = _service.GenerateSignature(_privateKeyHex, futureTimestamp, bodyBytes);

        var isValid = _service.VerifySignature(signatureHex, futureTimestamp, bodyBytes, _publicKeyHex);

        Assert.That(isValid, Is.True);
    }

    [TestCase("999999999999999999")]
    [TestCase("-999999999999999999")]
    [TestCase("9223372036854775807")]
    [TestCase("-9223372036854775808")]
    public void VerifySignature_should_return_false_without_throwing_for_extreme_timestamp_values(string extremeTimestamp)
    {
        var bodyBytes = Encoding.UTF8.GetBytes("{\"type\":1}");
        var signatureHex = new string('a', 128);

        Assert.DoesNotThrow(() =>
        {
            var isValid = _service.VerifySignature(signatureHex, extremeTimestamp, bodyBytes, _publicKeyHex);
            Assert.That(isValid, Is.False);
        });
    }
}
