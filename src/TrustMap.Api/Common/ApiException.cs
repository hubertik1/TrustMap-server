using System.Net;

namespace TrustMap.Api.Common;

public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, string title, string detail, string? errorCode = null)
        : base(detail)
    {
        StatusCode = (int)statusCode;
        Title = title;
        Detail = detail;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }

    public string Title { get; }

    public string Detail { get; }

    public string? ErrorCode { get; }

    public static ApiException BadRequest(string detail, string? errorCode = null) =>
        new(HttpStatusCode.BadRequest, "Bad Request", detail, errorCode);

    public static ApiException Unauthorized(string detail, string? errorCode = null) =>
        new(HttpStatusCode.Unauthorized, "Unauthorized", detail, errorCode);

    public static ApiException Forbidden(string detail, string? errorCode = null) =>
        new(HttpStatusCode.Forbidden, "Forbidden", detail, errorCode);

    public static ApiException NotFound(string detail, string? errorCode = null) =>
        new(HttpStatusCode.NotFound, "Not Found", detail, errorCode);

    public static ApiException Conflict(string detail, string? errorCode = null) =>
        new(HttpStatusCode.Conflict, "Conflict", detail, errorCode);

    public static ApiException TooManyRequests(string detail, string? errorCode = null) =>
        new(HttpStatusCode.TooManyRequests, "Too Many Requests", detail, errorCode);
}
