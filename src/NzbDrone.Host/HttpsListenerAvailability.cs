// Copyright (c) FeedItOut. All rights reserved.

using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;

namespace NzbDrone.Host;

/// <summary>
/// Tracks whether HTTPS was prepared successfully during bootstrap (certificate + Kestrel listen).
/// </summary>
public sealed class HttpsListenerAvailability
{
    public bool IsActive { get; private set; }

    public void SetActive(bool active)
    {
        IsActive = active;
    }

    public static bool TryPrepareCertificate(IConfigFileProvider config, ICertificateManager certManager)
    {
        if (config == null || certManager == null || !config.EnableSsl)
        {
            return false;
        }

        try
        {
            _ = certManager.GetOrCreateCertificate(config);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
