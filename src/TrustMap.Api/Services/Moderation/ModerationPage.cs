using System.Net;
using TrustMap.Domain.Entities;

namespace TrustMap.Api.Services.Moderation;

public static class ModerationPage
{
    public static string ActionLabel(string action) => action switch
    {
        "ignore" => "Ignoruj zgłoszenie",
        "remove" => "Usuń recenzję",
        "ban" => "Zablokuj konto autora",
        _ => "Moderacja"
    };

    public static string Confirm(ContentReport report, string action, string token, string csrfName, string csrfToken)
    {
        var images = string.Join("", report.Photos.Select(photo =>
            $"<p>Zdjęcie {photo.SourcePhotoId}</p><img alt=\"Zdjęcie recenzji\" src=\"data:image/jpeg;base64,{Convert.ToBase64String(photo.PreviewJpeg)}\">"));
        var password = action == "ban"
            ? "<label>Hasło administratora <input type=\"password\" name=\"adminPassword\" required autocomplete=\"current-password\" maxlength=\"256\"></label>"
            : "";
        return Layout($"<h1>{ActionLabel(action)}?</h1><p>Recenzja: {report.ReviewId}<br>Autor: {report.AuthorUserId}</p>"
            + (action == "ban" ? "<p>Konto zostanie zawieszone, a jego treści ukryte. To nie usuwa danych konta.</p>" : "")
            + $"<form method=\"post\" action=\"/moderation/{action}\"><input type=\"hidden\" name=\"token\" value=\"{WebUtility.HtmlEncode(token)}\">"
            + $"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(csrfName)}\" value=\"{WebUtility.HtmlEncode(csrfToken)}\">{password}"
            + $"<button type=\"submit\">Potwierdź: {ActionLabel(action)}</button></form>"
            + $"<h2>Zgłoszona wersja recenzji</h2><pre>{WebUtility.HtmlEncode(report.SnapshotJson)}</pre>{images}");
    }

    public static string Layout(string content) => "<!doctype html><html lang=\"pl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>TrustMap — moderacja</title>"
        + "<style>body{font:17px system-ui;max-width:760px;margin:32px auto;padding:0 20px;line-height:1.5}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f4f4f4;padding:16px}img{max-width:100%;height:auto}button,input{font:inherit;padding:12px;margin:12px 0;display:block}button{cursor:pointer}</style></head>"
        + $"<body>{content}</body></html>";
}
