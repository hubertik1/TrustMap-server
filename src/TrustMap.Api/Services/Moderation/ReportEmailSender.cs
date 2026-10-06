using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Options;
using TrustMap.Domain.Entities;

namespace TrustMap.Api.Services.Moderation;

public interface IReportEmailSender
{
    Task SendAsync(ContentReport report, CancellationToken cancellationToken);
}

public sealed class ReportEmailSender(IOptions<ModerationOptions> options, ModerationLinks links) : IReportEmailSender
{
    public async Task SendAsync(ContentReport report, CancellationToken cancellationToken)
    {
        using var message = BuildMessage(report);
        if (options.Value.EmailProvider == "file")
        {
            var directory = Path.Combine(options.Value.EmailDirectory, report.Id.ToString("N"));
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var pickup = new SmtpClient
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = Path.GetFullPath(directory),
                EnableSsl = false
            };
            // Pickup mode writes a complete MIME .eml file and never connects to
            // an SMTP server. Files are private and are not served by the API.
            await pickup.SendMailAsync(message, cancellationToken);
            if (!OperatingSystem.IsWindows())
                foreach (var file in Directory.EnumerateFiles(directory, "*.eml"))
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return;
        }
        var smtp = options.Value.Smtp;
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = string.IsNullOrEmpty(smtp.Username) ? null : new NetworkCredential(smtp.Username, smtp.Password)
        };
        await client.SendMailAsync(message, cancellationToken);
    }

    public MailMessage BuildMessage(ContentReport report)
    {
        var message = new MailMessage(options.Value.Smtp.SenderEmail, options.Value.RecipientEmail)
        {
            Subject = $"TrustMap: zgłoszenie {report.ReviewType} {report.ReviewId}",
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        var buttons = new StringBuilder();
        foreach (var action in ModerationLinks.Actions)
            buttons.Append($"<p><a href=\"{WebUtility.HtmlEncode(links.Create(report.Id, action))}\">{ModerationPage.ActionLabel(action)}</a></p>");

        var images = new StringBuilder();
        var included = new List<ReportPhoto>();
        long totalBytes = 0;
        foreach (var photo in report.Photos)
        {
            // Large reports retain all previews on the confirmation page, keeping
            // email size below typical delivery limits (including base64 overhead).
            if (totalBytes + photo.PreviewJpeg.Length > 6 * 1024 * 1024) break;
            totalBytes += photo.PreviewJpeg.Length;
            included.Add(photo);
            images.Append($"<p>Zdjęcie {photo.SourcePhotoId}</p><img alt=\"Zdjęcie recenzji\" width=\"400\" src=\"cid:photo-{photo.Id:N}\">");
        }
        var html = $"<html><body><h1>Zgłoszona recenzja TrustMap</h1><p>Zgłoszenie: {report.Id}<br>Data UTC: {report.CreatedAtUtc:O}<br>Autor: {report.AuthorUserId}</p>"
            + $"<p>Przyciski otwierają potwierdzenie. Ban wymaga hasła administratora. Linki wygasają po 7 dniach.</p>{buttons}"
            + $"<h2>Treść i dane recenzji</h2><pre>{WebUtility.HtmlEncode(report.SnapshotJson)}</pre><h2>Zdjęcia</h2>{images}"
            + (included.Count < report.Photos.Count ? "<p>Pozostałe zdjęcia są na stronie potwierdzenia.</p>" : "")
            + "<p>Zdjęcia są kopiami podglądowymi z chwili zgłoszenia. Wiadomość zawiera dane użytkownika — nie przekazuj jej dalej.</p></body></html>";
        var view = AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html);
        foreach (var photo in included)
            view.LinkedResources.Add(new LinkedResource(new MemoryStream(photo.PreviewJpeg, writable: false), "image/jpeg")
            {
                ContentId = $"photo-{photo.Id:N}", TransferEncoding = TransferEncoding.Base64
            });
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            $"Zgłoszenie {report.Id}\n{report.SnapshotJson}\n" + string.Join("\n", ModerationLinks.Actions.Select(action => $"{ModerationPage.ActionLabel(action)}: {links.Create(report.Id, action)}")),
            Encoding.UTF8, MediaTypeNames.Text.Plain));
        message.AlternateViews.Add(view);
        message.Attachments.Add(new Attachment(new MemoryStream(Encoding.UTF8.GetBytes(report.SnapshotJson)), "review.json", "application/json"));
        return message;
    }
}
