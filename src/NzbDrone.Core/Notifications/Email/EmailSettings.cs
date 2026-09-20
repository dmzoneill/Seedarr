namespace NzbDrone.Core.Notifications.Email;

public class EmailSettings
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";

    /// <summary>
    /// Comma-separated list of recipient email addresses.
    /// </summary>
    public string ToAddresses { get; set; } = "";

    public bool IgnoreSslErrors { get; set; }

    public bool AllowInvalidCertificates
    {
        get => IgnoreSslErrors;
        set => IgnoreSslErrors = value;
    }
}
