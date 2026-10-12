using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Qontract.API.Middleware;

/// <summary>
/// Catches unhandled exceptions anywhere in the pipeline and converts them into a
/// clean, consistent JSON error envelope so clients never see raw stack traces
/// (issue #11).
///
/// Exception -> response mapping:
///   KeyNotFoundException                     -> 404 not_found
///   FluentValidation.ValidationException     -> 400 validation_error
///   InvalidOperationException                -> 400 bad_request (client-triggered business rule)
///   UnauthorizedAccessException              -> 403 forbidden
///   anything else                            -> 500 internal_error (generic message, no internals)
///
/// In Development the response carries a <c>details</c> field with the exception
/// type and stack trace to aid debugging. In Production it is omitted.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                // Response streaming already began; we cannot replace the body.
                _logger.LogError(ex, "Exception after response started on {Method} {Path}",
                    context.Request.Method, context.Request.Path);
                throw;
            }

            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var (status, code, message) = MapException(ex);
        var details = _environment.EnvironmentName == "Development" ? new ExceptionDetails(ex) : null;

        if (status >= 500)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}: {Message}",
                context.Request.Method, context.Request.Path, ex.Message);
        }
        else
        {
            _logger.LogWarning("Handled exception on {Method} {Path}: {Type}: {Message}",
                context.Request.Method, context.Request.Path, ex.GetType().Name, ex.Message);
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ErrorResponse
        {
            Type = $"https://httpstatuses.org/{status}",
            Title = Qontract.API.Localization.UserMessages.Translate(GetTitle(status)),
            Status = status,
            Code = code,
            Message = Qontract.API.Localization.UserMessages.Translate(message),
            Details = details
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    internal static (int Status, string Code, string Message) MapException(Exception ex) => ex switch
    {
        // Use a generic message: the service message can leak cross-tenant
        // details (e.g. "not found in tenant 1"). Specifics are available via
        // the `details` field in Development.
        KeyNotFoundException => ((int)HttpStatusCode.NotFound, "not_found", "Resource not found."),
        FluentValidation.ValidationException => ((int)HttpStatusCode.BadRequest, "validation_error", ex.Message),
        InvalidOperationException => ((int)HttpStatusCode.BadRequest, "bad_request", ex.Message),
        UnauthorizedAccessException => ((int)HttpStatusCode.Forbidden, "forbidden", "Access to the requested resource is denied."),
        _ => ((int)HttpStatusCode.InternalServerError, "internal_error", "An unexpected error occurred. Please try again later.")
    };

    private static string GetTitle(int status) => status switch
    {
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "Error"
    };

    /// <summary>Development-only diagnostics; never present in Production responses.</summary>
    public sealed class ExceptionDetails
    {
        public string Type { get; }
        public string? StackTrace { get; }

        public ExceptionDetails(Exception ex)
        {
            Type = ex.GetType().Name;
            StackTrace = ex.StackTrace;
        }
    }

    /// <summary>RFC 7807-style error envelope returned by this middleware.</summary>
    public sealed class ErrorResponse
    {
        public string Type { get; init; } = "";
        public string Title { get; init; } = "";
        public int Status { get; init; }
        public string Code { get; init; } = "";
        public string Message { get; init; } = "";
        public ExceptionDetails? Details { get; init; }
    }
}
