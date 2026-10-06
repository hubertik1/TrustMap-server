using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace TrustMap.Api.Services.Moderation;

public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";
    public bool Enabled { get; set; }
    public string EmailProvider { get; set; } = "file";
    public string EmailDirectory { get; set; } = "App_Data/moderation-mails";
    public string DataProtectionKeyDirectory { get; set; } = "App_Data/moderation-keys";
    public string RecipientEmail { get; set; } = "hubert.mar@icloud.com";
    public string PublicBaseUrl { get; set; } = "";
    // SHA-256 hex digest of the separate administrator password. Never email it.
    public string AdminPasswordSha256 { get; set; } = "";
    public int MaxReportsPerUserPerDay { get; set; } = 20;
    public int EvidenceRetentionDays { get; set; } = 30;
    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string SenderEmail { get; set; } = "";
}

public sealed class ModerationOptionsValidator : IValidateOptions<ModerationOptions>
{
    public ValidateOptionsResult Validate(string? name, ModerationOptions options)
    {
        if (options.MaxReportsPerUserPerDay is < 1 or > 100
            || options.EvidenceRetentionDays is < 7 or > 90)
            return ValidateOptionsResult.Fail("Moderation limits are invalid.");
        if (!options.Enabled) return ValidateOptionsResult.Success;
        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 || url.AbsolutePath != "/")
            return ValidateOptionsResult.Fail("Moderation:PublicBaseUrl must be a trusted HTTPS origin without a path.");
        if (string.IsNullOrWhiteSpace(options.DataProtectionKeyDirectory))
            return ValidateOptionsResult.Fail("Moderation:DataProtectionKeyDirectory is required.");
        if (options.AdminPasswordSha256.Length != 0 && (options.AdminPasswordSha256.Length != 64 || !options.AdminPasswordSha256.All(Uri.IsHexDigit)))
            return ValidateOptionsResult.Fail("Moderation:AdminPasswordSha256 must be a SHA-256 password digest.");
        if (!MailAddress.TryCreate(options.RecipientEmail, out _) || !MailAddress.TryCreate(options.Smtp.SenderEmail, out _))
            return ValidateOptionsResult.Fail("Moderation sender and recipient must be valid email addresses.");
        if (options.EmailProvider == "file")
            return string.IsNullOrWhiteSpace(options.EmailDirectory)
                ? ValidateOptionsResult.Fail("Moderation:EmailDirectory is required.") : ValidateOptionsResult.Success;
        if (options.EmailProvider != "smtp")
            return ValidateOptionsResult.Fail("Moderation:EmailProvider must be file or smtp.");
        if (string.IsNullOrWhiteSpace(options.Smtp.Host) || options.Smtp.Port is < 1 or > 65535 || !options.Smtp.EnableSsl)
            return ValidateOptionsResult.Fail("Moderation SMTP host, sender, recipient and TLS are required.");
        return ValidateOptionsResult.Success;
    }
}
