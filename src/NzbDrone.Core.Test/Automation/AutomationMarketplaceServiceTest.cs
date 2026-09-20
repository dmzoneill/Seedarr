#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Automation;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class AutomationMarketplaceServiceTest
{
    private const string TestPrivateKeyPem =
@"-----BEGIN PRIVATE KEY-----
MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQDs6vy1rZ35n73F
gCpKdXiloIUeAr9QylxpZNS2TmbL32M2PThBL9zW1SSZA+XMB4FWgbrnLG+gUQqm
Kn3SB+LdxgkvDHcR2LSjMwDMCpp7xgmkbjEToFYUXW7NXwGEikHbLg5nFgGuukAF
cBuysZjN2kBRsuh8zrRPafGxsGfaublgp6CMdpVFbZDR+n1bwBjYc22JLEoqyr7j
l/cDj7Ch0/26hP3cIzdEgiyr/Uotu0iivFBgGVO8Y4xzS9gjSa3eG0zyTVocQiLq
qJAP+6BpLmbhEMPxj9n45y8Z0K1rjmilli4igUmWkhJprSG9X2AuPr3I3RRC81rt
swFSVMNnAgMBAAECggEAWeDp3wiPAljCFy+Am7/F9duYNKLpLn6eoVMhyUmkANrt
2fFOqpg8SPoSSMRsRMNiI03S+IlojaLBDfnXwrrAK89Jw6IQd+JE4RbjygSJx8QA
+3LcRmxQy6gIdXwB6OTEiCCIUT5NhBpfWFDICToL1KhkNzpOra6DRA9WpEjelWCd
/fLwkUSQZ9zVYn0JHkarE+mz61VU8J2qqOFu2l/GW5dkbt9AfQlQpY1GrIuk/dFK
HAdhI0sd2WNsDiJN5yfBJQv7FBF+Z3pBi9De3nDhzqDwzNIW7LVa8eOysUeCC8SC
CQScj+/kpS/Oy6lxWWD9lVG5YBsqL/sKBmN14eX+vQKBgQD/+7GBq0FKiVtjMZXM
qE8J2QITvCFfbs2Z2jKeUD1zFGHonHuGZkklel/mNfDCCJkvzkfKD9Al8XA6+013
R3amYgfdDybzg9BjTPDMqRyinwz3GxvLCbMMcFiSFabseCZBSuRu6yX6yS+d5QnQ
Jk9TgrOJiwGRRhsTgNqomhq1YwKBgQDs7vkXTe3PmRXzC7IBrPOpnclg39YoyZrk
MXQ3slTaeCuTeD+/LGXO71dwh6SCfMkX+ia4urLsfyODH6e4tcA8G48lB8D4C8Z2
uwsiGhYkiSK+uaNdIpy5sHXQJPij/RsQm9bwAu/d5MyzfMtsicgcypsEh16uveDs
R7cxmqvrLQKBgQDcdG4N52gmgh7zUYvaIoVXTM4OIvJ21t04iAMZ9q7FQiOECegT
+lk6bqbSg1fiMzeCRVvsOCwh0Um/chfoBuK3LivphJgeFkJMksG68FWZ8/Jdiboo
5SSPLN4KiZ0lf+AqUQ5kPB2MWuGoUM1ftu6QVDq81Ls32rGM3Wby1yzzUwKBgCvP
TDOo7y4RqmNUaEezCSL10ASfnuPP01oaYtjhmpsC2VvdQjxBI1oOG2btTdfq5uwO
DxbdPrRIFfLq6YJX6QG0PtWkB2RWGQ5fK4HUvP9odAo8HR7dhYk4PbCNBYSdCmIZ
zrZ2dI/c/JS7oImjOGNKezttJG6/IVXPNOpxJRTJAoGBAPjdniZ/mCxBveJnyO1i
Vbobo4EVtHjjt+atU6VtMKxI08NCeAXmLrbBxbONU8XWSPlAajbxN9hFqbg5MzVt
raeAyBdrU0nTfFoCjIgN4yTks6DpsnMrJeC4ADf3FhsQ+OJpGPPzagGR6TMVzEFh
IOEQeL0BsJ0s81a4MEkdWceZ
-----END PRIVATE KEY-----";

    private AutomationMarketplaceService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new AutomationMarketplaceService();
    }

    [Test]
    public void InstallTemplate_SetsIsEnabledToFalse_ByDefault()
    {
        var script = _service.InstallTemplate("auto-tag-by-ratio-pause");

        Assert.That(script, Is.Not.Null);
        Assert.That(script.IsEnabled, Is.False, "Installed templates must be disabled by default for security review.");
    }

    [Test]
    public void GetTemplates_ReturnsTemplatesWithVerifiedSignaturesAndCapabilities()
    {
        var templates = _service.GetTemplates();

        Assert.That(templates, Has.Count.GreaterThanOrEqualTo(5));
        foreach (var tmpl in templates)
        {
            Assert.That(tmpl.Sha256, Is.Not.Empty);
            Assert.That(tmpl.Signature, Is.Not.Empty);
            Assert.That(tmpl.IsVerified, Is.True, $"Template {tmpl.Id} signature should be verified.");
            Assert.That(tmpl.Capabilities, Is.Not.Empty);
        }
    }

    [Test]
    public void InstallTemplate_ThrowsException_WhenSha256HashMismatch()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "tampered-template",
            Version = "1.0.0",
            Language = AutomationLanguage.JavaScript,
            Trigger = AutomationTrigger.TorrentAdded,
            Code = "console.log('legit');",
            Sha256 = "invalidhash1234567890",
            Signature = "dummy",
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            AutomationMarketplaceService.VerifyTemplateIntegrity(tmpl);
        });

        Assert.That(ex!.Message, Does.Contain("Content hash mismatch"));
    }

    [Test]
    public void InstallTemplate_ThrowsException_WhenSignatureIsInvalid()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "tampered-sig-template",
            Version = "1.0.0",
            Language = AutomationLanguage.JavaScript,
            Trigger = AutomationTrigger.TorrentAdded,
            Code = "console.log('tampered');",
            Publisher = "Seedarr Official",
        };
        tmpl.Sha256 = AutomationMarketplaceService.ComputeCanonicalHash(tmpl);
        tmpl.Signature = Convert.ToBase64String(new byte[256]); // Invalid signature bytes

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            AutomationMarketplaceService.VerifyTemplateIntegrity(tmpl);
        });

        Assert.That(ex!.Message, Does.Contain("signature verification failed"));
    }

    [Test]
    public void SignTemplate_ProducesValidSignature_VerifiableByPublicKey()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "custom-signed-workflow",
            Version = "1.0.0",
            Language = AutomationLanguage.JavaScript,
            Trigger = AutomationTrigger.TorrentCompleted,
            Code = "console.log('custom');",
            Publisher = "Seedarr Official",
        };

        AutomationMarketplaceService.SignTemplate(tmpl, TestPrivateKeyPem);

        Assert.That(tmpl.Sha256, Is.Not.Empty);
        Assert.That(tmpl.Signature, Is.Not.Empty);
        Assert.That(tmpl.IsVerified, Is.True);
        Assert.DoesNotThrow(() => AutomationMarketplaceService.VerifyTemplateIntegrity(tmpl));
    }

    [Test]
    public void ValidateAndResolveInputs_ThrowsException_WhenRequiredInputIsMissing()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "required-test",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "api_key", Label = "API Key", Type = "text", DefaultValue = string.Empty, Required = true },
            },
        };

        var ex = Assert.Throws<ArgumentException>(() =>
        {
            AutomationMarketplaceService.ValidateAndResolveInputs(tmpl, new Dictionary<string, string>());
        });

        Assert.That(ex!.Message, Does.Contain("is required"));
    }

    [Test]
    public void ValidateAndResolveInputs_ThrowsException_WhenInputIsNotValidNumber()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "number-test",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "ratio", Label = "Ratio", Type = "number", DefaultValue = "1.0", Required = true },
            },
        };

        var customInputs = new Dictionary<string, string>
        {
            { "ratio", "not_a_number" },
        };

        var ex = Assert.Throws<ArgumentException>(() =>
        {
            AutomationMarketplaceService.ValidateAndResolveInputs(tmpl, customInputs);
        });

        Assert.That(ex!.Message, Does.Contain("must be a valid number"));
    }

    [Test]
    public void ValidateAndResolveInputs_ThrowsException_WhenInputIsNotValidUrl()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "url-test",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "webhook", Label = "Webhook URL", Type = "url", DefaultValue = "https://example.com", Required = true },
            },
        };

        var customInputs = new Dictionary<string, string>
        {
            { "webhook", "ftp://invalid-scheme" },
        };

        var ex = Assert.Throws<ArgumentException>(() =>
        {
            AutomationMarketplaceService.ValidateAndResolveInputs(tmpl, customInputs);
        });

        Assert.That(ex!.Message, Does.Contain("must be a valid HTTP or HTTPS URL"));
    }

    [Test]
    public void ValidateAndResolveInputs_ThrowsException_WhenSelectValueIsNotAllowed()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "select-test",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "mode", Label = "Mode", Type = "select", DefaultValue = "pause", AllowedValues = new List<string> { "pause", "resume" }, Required = true },
            },
        };

        var customInputs = new Dictionary<string, string>
        {
            { "mode", "invalid_action" },
        };

        var ex = Assert.Throws<ArgumentException>(() =>
        {
            AutomationMarketplaceService.ValidateAndResolveInputs(tmpl, customInputs);
        });

        Assert.That(ex!.Message, Does.Contain("must be one of"));
    }

    [Test]
    public void ValidateAndResolveInputs_ThrowsException_WhenStringExceedsMaxLength()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "bounds-test",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "short_code", Label = "Short Code", Type = "text", DefaultValue = "ok", MaxLength = 5, Required = true },
            },
        };

        var customInputs = new Dictionary<string, string>
        {
            { "short_code", "1234567890" },
        };

        var ex = Assert.Throws<ArgumentException>(() =>
        {
            AutomationMarketplaceService.ValidateAndResolveInputs(tmpl, customInputs);
        });

        Assert.That(ex!.Message, Does.Contain("exceeds maximum allowed length"));
    }

    [Test]
    public void ValidateAndResolveInputs_ThrowsException_WhenUnrecognizedKeyProvided()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "unknown-key-test",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "field1", Label = "Field 1", Type = "text", DefaultValue = "val", Required = true },
            },
        };

        var customInputs = new Dictionary<string, string>
        {
            { "field1", "valid" },
            { "injected_param", "payload" },
        };

        var ex = Assert.Throws<ArgumentException>(() =>
        {
            AutomationMarketplaceService.ValidateAndResolveInputs(tmpl, customInputs);
        });

        Assert.That(ex!.Message, Does.Contain("Unrecognized input parameter"));
    }

    [Test]
    public void DetectCapabilities_DetectsHttpAndTorrentMutation()
    {
        var tmpl = new AutomationMarketplaceTemplate
        {
            Id = "cap-test",
            Code = "http.post('url'); torrent.pause();",
        };

        var caps = AutomationMarketplaceService.DetectCapabilities(tmpl);

        Assert.That(caps, Does.Contain("Executes HTTP requests"));
        Assert.That(caps, Does.Contain("Mutates torrent state"));
    }
}
