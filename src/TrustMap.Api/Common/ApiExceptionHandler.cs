using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Common;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail, errorCode) = exception switch
        {
            ApiException apiException => (apiException.StatusCode, apiException.Title, apiException.Detail, apiException.ErrorCode),
            PhotoStorageException photoStorageException => (
                StatusCodes.Status400BadRequest,
                "Bad Request",
                photoStorageException.Message,
                photoStorageException.ErrorCode),
            InvalidDataException invalidDataException => (
                StatusCodes.Status400BadRequest,
                "Bad Request",
                invalidDataException.Message,
                "invalid_request_body"),
            ArgumentOutOfRangeException argumentOutOfRangeException => (
                StatusCodes.Status400BadRequest,
                "Bad Request",
                argumentOutOfRangeException.Message,
                "invalid_argument"),
            InvalidOperationException invalidOperationException => (
                StatusCodes.Status400BadRequest,
                "Bad Request",
                invalidOperationException.InnerException is null
                    ? invalidOperationException.Message
                    : $"{invalidOperationException.Message} Inner: {invalidOperationException.InnerException.Message}",
                "invalid_operation"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.", "unexpected_error")
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing request.");
        }
        else
        {
            logger.LogWarning(exception, "Request failed with status code {StatusCode}.", statusCode);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = statusCode;
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            problemDetails.Extensions["errorCode"] = errorCode;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
