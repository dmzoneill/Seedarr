#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Automation;

public class AutomationMarketplaceTemplate
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Author { get; set; } = "Community";

    public string Version { get; set; } = "1.0.0";

    public string Category { get; set; } = "General";

    public AutomationTrigger Trigger { get; set; } = AutomationTrigger.TorrentAdded;

    public AutomationLanguage Language { get; set; } = AutomationLanguage.JavaScript;

    public string Code { get; set; } = string.Empty;

    public Dictionary<string, string> DefaultInputs { get; set; } = new();

    public List<TemplateInputField> InputFields { get; set; } = new();

    public string Sha256 { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    public string Publisher { get; set; } = "Seedarr Official";

    public bool IsVerified { get; set; }

    public List<string> Capabilities { get; set; } = new();
}

public class TemplateInputField
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Type { get; set; } = "text"; // text, password, number, select, boolean, url

    public string DefaultValue { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Required { get; set; } = true;

    public string? RegexPattern { get; set; }

    public List<string>? AllowedValues { get; set; }

    public int MaxLength { get; set; } = 2048;
}

public interface IAutomationMarketplaceService
{
    List<AutomationMarketplaceTemplate> GetTemplates();

    AutomationMarketplaceTemplate? GetTemplate(string templateId);

    AutomationScript InstallTemplate(string templateId, string? customName = null, Dictionary<string, string>? customInputs = null);
}

public class AutomationMarketplaceService : IAutomationMarketplaceService
{
    public const string OfficialPublisherPublicKeyPem =
@"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA7Or8ta2d+Z+9xYAqSnV4
paCFHgK/UMpcaWTUtk5my99jNj04QS/c1tUkmQPlzAeBVoG65yxvoFEKpip90gfi
3cYJLwx3Edi0ozMAzAqae8YJpG4xE6BWFF1uzV8BhIpB2y4OZxYBrrpABXAbsrGY
zdpAUbLofM60T2nxsbBn2rm5YKegjHaVRW2Q0fp9W8AY2HNtiSxKKsq+45f3A4+w
odP9uoT93CM3RIIsq/1KLbtIorxQYBlTvGOMc0vYI0mt3htM8k1aHEIi6qiQD/ug
aS5m4RDD8Y/Z+OcvGdCta45opZYuIoFJlpISaa0hvV9gLj69yN0UQvNa7bMBUlTD
ZwIDAQAB
-----END PUBLIC KEY-----";

    private static readonly List<AutomationMarketplaceTemplate> BuiltinTemplates = new()
    {
        new AutomationMarketplaceTemplate
        {
            Id = "pt-auto-zap-tokens",
            Name = "Private Tracker Auto-Zap & Token Spender",
            Description = "Logs into private tracker using session cookie, checks if torrent can be zapped/freed with bonus tokens, and triggers the zap action.",
            Author = "Seedarr Community",
            Publisher = "Seedarr Official",
            Version = "1.1.0",
            Category = "Private Trackers",
            Trigger = AutomationTrigger.TorrentAdded,
            Language = AutomationLanguage.JavaScript,
            Sha256 = "28deac7c3ed991a5a24d0d79209a546d7448cf5afdb1fc929c19ea205e6c0d89",
            Signature = "dw5IpVJ+MpPFJ+B+eL+IVsWUEEa4zIh2cAe5qGYkmV7jaalr+r8x029K5lyu+LlGYOdx+pPz/R5AhOfenGOLUztEQu7t0BOezSweQmWrv0Z8CVfYX5CT7AvIeYVaii2ey9r554cM5XzzKJyW03pak6jVv0G3xBVNUZOSGEjt5t0NEcvhCrxVeYhogW55ZUztgWde1A68CXo8457yiw46uQIXj/92l83YdatOTbtTPVSz2taP/VIECfhoBV65q1hNa5CdE6HR5Qxzvj9QqR5AcpP6QtJSqbqal+031US/GLgT9qKrupuOOVP6/LBOQsDcIjxtvnIblXa8Zc5NZaM35Q==",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "tracker_domain", Label = "Tracker Domain", Type = "text", DefaultValue = "tracker.example.com", Description = "Domain to match against torrent tracker URL", Required = true, MaxLength = 253 },
                new() { Key = "session_cookie", Label = "Session Cookie / API Token", Type = "password", DefaultValue = string.Empty, Description = "Your private tracker session cookie or API key", Required = false, MaxLength = 4096 },
                new() { Key = "zap_tag", Label = "Applied Tag", Type = "text", DefaultValue = "zapped", Description = "Tag applied to the torrent once zapped", Required = true, MaxLength = 100 },
            },
            Code = @"// Private Tracker Auto-Zap Script
if (!torrent.tracker.includes(inputs.tracker_domain)) {
    console.log('Skipping: tracker does not match ' + inputs.tracker_domain);
    return;
}

console.log('Processing torrent: ' + torrent.name + ' (' + torrent.infoHash + ')');

var response = http.post('https://' + inputs.tracker_domain + '/api/torrent/' + torrent.infoHash + '/zap', {
    torrent_id: torrent.infoHash
}, {
    headers: {
        'Authorization': 'Bearer ' + inputs.session_cookie,
        'Cookie': 'session=' + inputs.session_cookie
    }
});

if (response.ok) {
    console.log('Successfully zapped torrent on tracker!');
    torrent.addTag(inputs.zap_tag || 'zapped');
} else {
    console.warn('Zap request returned status: ' + response.status + ' - ' + response.body);
}
",
        },
        new AutomationMarketplaceTemplate
        {
            Id = "auto-tag-by-ratio-pause",
            Name = "Seed Ratio Goal & Auto-Pause",
            Description = "Monitors torrent ratio; when target seed ratio is satisfied, tags torrent as 'ratio-met' and pauses seeding.",
            Author = "Seedarr Community",
            Publisher = "Seedarr Official",
            Version = "1.0.0",
            Category = "Ratio Management",
            Trigger = AutomationTrigger.RatioReached,
            Language = AutomationLanguage.JavaScript,
            Sha256 = "87f8e221d76b85565dddcf71c749ed9558db8443d47510fc502c4fb95cbe7a84",
            Signature = "7EdiKSkhZ63xncc/4uBaz1OP6O7L6ngG06o+JkNNyJsBsCVXY0yJDjVwLZwI0YD6tczPChUbU5x3TklMzPDYPZ2OSBDJ2PLLD0A9ncWZRPGJmp1Ra99lKVtJ2VE+4h2/hLNft7mzBBhlNF2xIJbR5YGWmUIVkttxpdRkKhyunJZ9yAzl407FdnCzvnS+VyyHtVvhwaAYHksNY0rSte6qTKz5B/fQEDXdcxTwhdmTwXg6ouFUcbONDhV/p8ObrtQcEAUdL/6F74LmGoppYFMynbO/T3jayPLnfAklHYasFepjpR+h7ZkxoJTfcAIJAEfR36z0yP85qEvVDOEkTktw6w==",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "target_ratio", Label = "Target Ratio", Type = "number", DefaultValue = "3.0", Description = "Ratio threshold to trigger auto-pause", Required = true },
                new() { Key = "tag_name", Label = "Completed Tag", Type = "text", DefaultValue = "ratio-met", Description = "Tag applied when target ratio is reached", Required = true, MaxLength = 100 },
            },
            Code = @"// Seed Ratio Auto-Pause Script
var targetRatio = parseFloat(inputs.target_ratio || 3.0);

console.log('Torrent ' + torrent.name + ' current ratio: ' + torrent.ratio + ' (Target: ' + targetRatio + ')');

if (torrent.ratio >= targetRatio) {
    console.log('Target ratio reached! Adding tag and pausing torrent.');
    torrent.addTag(inputs.tag_name || 'ratio-met');
    torrent.pause();
}
",
        },
        new AutomationMarketplaceTemplate
        {
            Id = "discord-download-webhook",
            Name = "Discord Webhook Notification on Complete",
            Description = "Sends a rich embed notification to your Discord channel when a download completes.",
            Author = "Seedarr Community",
            Publisher = "Seedarr Official",
            Version = "1.2.0",
            Category = "Notifications",
            Trigger = AutomationTrigger.TorrentCompleted,
            Language = AutomationLanguage.JavaScript,
            Sha256 = "9954d988de1659ad470db387acda6e9ab781d76db9d176d988928d66715e6c70",
            Signature = "7Kx49Oza6gI1D7GzGM9/Polq5N9+p0fXAHzTJx8L9EXmfwjn0rxV88ChVmd+wSIwmt5+fA/OxmRG0c0gmeGNZOZb8R1ATkLUGnsyrKTePczZnxzDV6ol/P1EmpgtjnZI3g+oamEJsgUnl+ivNJ+aE9F8+SIN2MOtq/51fqeTveomqrYvqjxNND3KCVOzwVmk1YhIur7OUJHuU1KqkI0kmNjF+nrj14lFyCabdc07U2t5iedoMWRyD89cIMc7XgSfpez2lkhCCuKLsjrCJlVWJDJkfrTRfJu/vX1JqA/1qVmKSn+bSOwqRGuK3a9GRRPXwY7rVYR3OL3wmV5QQgNypQ==",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "webhook_url", Label = "Discord Webhook URL", Type = "url", DefaultValue = "https://discord.com/api/webhooks/placeholder", Description = "Your Discord webhook endpoint", Required = true, MaxLength = 2048 },
            },
            Code = @"// Discord Webhook Notification
if (!inputs.webhook_url) {
    console.error('Discord webhook URL is not configured!');
    return;
}

var sizeMB = (torrent.size / (1024 * 1024)).toFixed(2);

var payload = {
    embeds: [{
        title: 'Download Completed',
        description: '**' + torrent.name + '**',
        color: 3066993, // Green
        fields: [
            { name: 'Size', value: sizeMB + ' MB', inline: true },
            { name: 'Category', value: torrent.category || 'Default', inline: true },
            { name: 'Ratio', value: torrent.ratio.toFixed(2), inline: true }
        ],
        timestamp: new Date().toISOString()
    }]
};

var res = http.post(inputs.webhook_url, payload, { json: true });
if (res.ok) {
    console.log('Discord notification dispatched successfully.');
} else {
    console.error('Failed to dispatch Discord webhook: ' + res.status);
}
",
        },
        new AutomationMarketplaceTemplate
        {
            Id = "yaml-auto-categorize-media",
            Name = "Declarative Media Categorizer (YAML)",
            Description = "Non-programmer YAML workflow that checks media resolution in name and assigns Movie or TV categories.",
            Author = "Seedarr Community",
            Publisher = "Seedarr Official",
            Version = "1.0.0",
            Category = "Organization",
            Trigger = AutomationTrigger.TorrentAdded,
            Language = AutomationLanguage.Yaml,
            Sha256 = "f6d7037693278cca7817f7de835e78f392dd728444e5e143e4d72416cd166fa4",
            Signature = "IY/b7q8paSbyYApgWBh7hZIxF0QuHAEi6fpCS0+kSRMF5z0BmzpnMwe0qdk6xpREaBvgAuKtrsSYeBn/asOhrzxwgCM1z6wmsDK9dfp4vIBWZhHrHMWf6tSJ1JPdjJodVTbOgcJo6S5Og9xei1bt1qTOQCjJX8pcj6ER7cmO7pWmTzRB0n+zXz1r0Q5CaiiHaDRi0ZS3U8dWUXpWQCLWB7jQAf0WwyLPImmj/l7CCYaTze8+WwlD5/ynVYjQjJHvR5TF027pIsx6ML1zKMhTM8U2pNYnKZqkRqo5bqACkJB0vRGoeVfyw63hg80Nix0OaJumH2qUZkZu4VDoQ13WCg==",
            InputFields = new List<TemplateInputField>(),
            Code = "name: 'Auto Categorize Media'\n" +
                   "trigger: 'TorrentAdded'\n" +
                   "steps:\n" +
                   "  - name: 'Check for TV Show'\n" +
                   "    condition: '${torrent.name}'\n" +
                   "    actions:\n" +
                   "      - addTag: 'Automated'\n\n" +
                   "  - name: 'Tag Fast Seeding'\n" +
                   "    condition: '${torrent.size} > 1000000000'\n" +
                   "    actions:\n" +
                   "      - addTag: 'LargeTorrent'\n",
        },
        new AutomationMarketplaceTemplate
        {
            Id = "stalled-torrent-pruner",
            Name = "Stalled Torrent Pruner & Cleaner",
            Description = "Checks if torrent has zero upload speed and has been inactive, applying a 'stalled' tag or pausing it.",
            Author = "Seedarr Community",
            Publisher = "Seedarr Official",
            Version = "1.0.0",
            Category = "Maintenance",
            Trigger = AutomationTrigger.Scheduled,
            Language = AutomationLanguage.JavaScript,
            Sha256 = "1e964bda061e766cff7c733c95e02a0adc722e05beb9fcb61f0802ba82b8971f",
            Signature = "aqkUK6i3lCr5qDy5T7JkJD5WTVFuh/uhbw2Ws1OiuJn2VaKAoSR8VsjEPfMG5uHZFaMS4bMjs26IZmBEm5fVk0zCRuMJUaWbzhPtUlsk5iN7CiTOp3WMUDuehfXhfXJplIzCTKJ0DPy4g54c+PWRhLFx2nx48OfnldKO1ZxB6zPLZ6fvpZ6yh7sMKmSS7YRgG9pvYJJRatrESSFJhNxBa41KFgeyjJb70UqjmfS6USrXH2xrSRCI/E94d1wRZ4aaVxbpp7Yv/h0ZvWCUaXtkriXG2HL9eWRq0uyczIl7aZrwGhIUt0tEPrJyXcCNxu/wDdzSoQ9fru2mzXGEURUI3g==",
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "action", Label = "Action (pause or tag)", Type = "select", DefaultValue = "tag", Description = "Whether to pause or tag stalled torrents", Required = true, AllowedValues = new List<string> { "tag", "pause" } },
            },
            Code = @"// Stalled Torrent Pruner
if (torrent && torrent.uploadSpeed === 0 && torrent.downloadSpeed === 0) {
    console.log('Torrent ' + torrent.name + ' has 0 B/s traffic.');
    if (inputs.action === 'pause') {
        torrent.pause();
    } else {
        torrent.addTag('stalled');
    }
}
",
        },
    };

    static AutomationMarketplaceService()
    {
        foreach (var template in BuiltinTemplates)
        {
            if (template.DefaultInputs.Count == 0 && template.InputFields.Count > 0)
            {
                template.DefaultInputs = template.InputFields.ToDictionary(f => f.Key, f => f.DefaultValue);
            }

            template.Capabilities = DetectCapabilities(template);
            template.IsVerified = VerifyTemplate(template);
        }
    }

    public static string ComputeCanonicalHash(AutomationMarketplaceTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var normalizedCode = (template.Code ?? string.Empty).Replace("\r\n", "\n").Trim();
        var inputSummary = template.InputFields != null
            ? string.Join(";", template.InputFields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}:{f.Type}:{f.Required}"))
            : string.Empty;

        var canonicalText = $"{template.Id}\n{template.Version}\n{template.Language}\n{template.Trigger}\n{inputSummary}\n{normalizedCode}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static bool VerifySignature(string sha256Hex, string signatureBase64, string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(sha256Hex) || string.IsNullOrWhiteSpace(signatureBase64) || string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var hashBytes = Convert.FromHexString(sha256Hex);
            var signatureBytes = Convert.FromBase64String(signatureBase64);
            return rsa.VerifyHash(hashBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    public static string SignHash(string sha256Hex, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(sha256Hex);
        ArgumentNullException.ThrowIfNull(privateKeyPem);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var hashBytes = Convert.FromHexString(sha256Hex);
        var signatureBytes = rsa.SignHash(hashBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signatureBytes);
    }

    public static AutomationMarketplaceTemplate SignTemplate(AutomationMarketplaceTemplate template, string privateKeyPem, string publisher = "Seedarr Official")
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(privateKeyPem);

        template.Publisher = publisher;
        template.Sha256 = ComputeCanonicalHash(template);
        template.Signature = SignHash(template.Sha256, privateKeyPem);
        template.IsVerified = true;
        return template;
    }

    public static bool VerifyTemplate(AutomationMarketplaceTemplate template, string? trustedPublicKeyPem = null)
    {
        if (string.IsNullOrWhiteSpace(template.Sha256) || string.IsNullOrWhiteSpace(template.Signature))
        {
            return false;
        }

        var computedHash = ComputeCanonicalHash(template);
        if (!string.Equals(computedHash, template.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return VerifySignature(template.Sha256, template.Signature, trustedPublicKeyPem ?? OfficialPublisherPublicKeyPem);
    }

    public static void VerifyTemplateIntegrity(AutomationMarketplaceTemplate template, string? trustedPublicKeyPem = null)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (string.IsNullOrWhiteSpace(template.Sha256))
        {
            throw new InvalidOperationException($"Template '{template.Id}' cannot be installed: missing integrity hash.");
        }

        var computedHash = ComputeCanonicalHash(template);
        if (!string.Equals(computedHash, template.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Template integrity check failed for '{template.Id}'. Content hash mismatch.");
        }

        if (string.Equals(template.Publisher, "Seedarr Official", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(template.Signature))
        {
            if (string.IsNullOrWhiteSpace(template.Signature))
            {
                throw new InvalidOperationException($"Template '{template.Id}' claims to be official but is missing a publisher signature.");
            }

            var isValid = VerifySignature(template.Sha256, template.Signature, trustedPublicKeyPem ?? OfficialPublisherPublicKeyPem);
            if (!isValid)
            {
                throw new InvalidOperationException($"Template cryptographic signature verification failed for '{template.Id}'. Untrusted or tampered publisher signature.");
            }
        }
    }

    public static Dictionary<string, string> ValidateAndResolveInputs(AutomationMarketplaceTemplate template, Dictionary<string, string>? customInputs)
    {
        ArgumentNullException.ThrowIfNull(template);

        var inputs = new Dictionary<string, string>();
        var fieldMap = template.InputFields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

        if (customInputs != null)
        {
            foreach (var key in customInputs.Keys)
            {
                if (!fieldMap.ContainsKey(key))
                {
                    throw new ArgumentException($"Unrecognized input parameter '{key}'.");
                }
            }
        }

        foreach (var field in template.InputFields)
        {
            string? val = null;
            if (customInputs != null && customInputs.TryGetValue(field.Key, out var customVal))
            {
                val = customVal;
            }
            else
            {
                val = field.DefaultValue;
            }

            val ??= string.Empty;

            if (field.Required && string.IsNullOrWhiteSpace(val))
            {
                throw new ArgumentException($"Input parameter '{field.Label}' ({field.Key}) is required.");
            }

            if (!string.IsNullOrEmpty(val))
            {
                var maxLen = field.MaxLength > 0 ? field.MaxLength : 2048;
                if (val.Length > maxLen)
                {
                    throw new ArgumentException($"Input parameter '{field.Label}' ({field.Key}) exceeds maximum allowed length of {maxLen} characters.");
                }

                switch (field.Type.ToLowerInvariant())
                {
                    case "number":
                        if (!double.TryParse(val, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
                        {
                            throw new ArgumentException($"Input parameter '{field.Label}' ({field.Key}) must be a valid number.");
                        }

                        break;

                    case "boolean":
                        if (!bool.TryParse(val, out _) && val != "0" && val != "1")
                        {
                            throw new ArgumentException($"Input parameter '{field.Label}' ({field.Key}) must be a boolean ('true' or 'false').");
                        }

                        break;

                    case "url":
                        if (!Uri.TryCreate(val, UriKind.Absolute, out var uriResult) ||
                            (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
                        {
                            throw new ArgumentException($"Input parameter '{field.Label}' ({field.Key}) must be a valid HTTP or HTTPS URL.");
                        }

                        break;

                    case "select":
                        if (field.AllowedValues != null && field.AllowedValues.Count > 0)
                        {
                            if (!field.AllowedValues.Contains(val, StringComparer.OrdinalIgnoreCase))
                            {
                                throw new ArgumentException($"Input parameter '{field.Label}' ({field.Key}) must be one of: {string.Join(", ", field.AllowedValues)}.");
                            }
                        }

                        break;
                }

                if (!string.IsNullOrWhiteSpace(field.RegexPattern))
                {
                    var regex = new Regex(field.RegexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
                    if (!regex.IsMatch(val))
                    {
                        throw new ArgumentException($"Input parameter '{field.Label}' ({field.Key}) format is invalid.");
                    }
                }
            }

            inputs[field.Key] = val;
        }

        return inputs;
    }

    public static List<string> DetectCapabilities(AutomationMarketplaceTemplate template)
    {
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var code = template.Code ?? string.Empty;

        if (code.Contains("http.", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("http:", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("fetch(", StringComparison.OrdinalIgnoreCase))
        {
            capabilities.Add("Executes HTTP requests");
        }

        if (code.Contains("torrent.pause", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("torrent.resume", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("torrent.addTag", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("torrent.removeTag", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("torrent.remove", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("addTag:", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("removeTag:", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("pauseTorrent", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("setCategory:", StringComparison.OrdinalIgnoreCase))
        {
            capabilities.Add("Mutates torrent state");
        }

        if (code.Contains("system.runCommand", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("command:", StringComparison.OrdinalIgnoreCase))
        {
            capabilities.Add("Runs external commands");
        }

        if (capabilities.Count == 0)
        {
            capabilities.Add("Read-only workflow");
        }

        return capabilities.OrderBy(c => c).ToList();
    }

    public List<AutomationMarketplaceTemplate> GetTemplates()
    {
        foreach (var tmpl in BuiltinTemplates)
        {
            tmpl.DefaultInputs = tmpl.InputFields.ToDictionary(f => f.Key, f => f.DefaultValue);
            tmpl.Capabilities = DetectCapabilities(tmpl);
            tmpl.IsVerified = VerifyTemplate(tmpl);
        }

        return BuiltinTemplates;
    }

    public AutomationMarketplaceTemplate? GetTemplate(string templateId)
    {
        var template = BuiltinTemplates.FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));
        if (template != null)
        {
            template.DefaultInputs = template.InputFields.ToDictionary(f => f.Key, f => f.DefaultValue);
            template.Capabilities = DetectCapabilities(template);
            template.IsVerified = VerifyTemplate(template);
        }

        return template;
    }

    public AutomationScript InstallTemplate(string templateId, string? customName = null, Dictionary<string, string>? customInputs = null)
    {
        var template = GetTemplate(templateId) ?? throw new InvalidOperationException($"Template with ID '{templateId}' was not found.");

        VerifyTemplateIntegrity(template);

        var inputs = ValidateAndResolveInputs(template, customInputs);

        return new AutomationScript
        {
            Name = !string.IsNullOrWhiteSpace(customName) ? customName.Trim() : template.Name,
            Description = template.Description,
            Trigger = template.Trigger,
            Language = template.Language,
            Code = template.Code,
            InputsJson = System.Text.Json.JsonSerializer.Serialize(inputs),
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow,
        };
    }
}
