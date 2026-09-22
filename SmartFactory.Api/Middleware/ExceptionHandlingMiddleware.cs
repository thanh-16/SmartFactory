using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartFactory.Api.Exceptions;

namespace SmartFactory.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);
            await HandleExceptionAsync(context, exception);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var problemDetails = exception switch
        {
            InvalidFileFormatException fileException => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid File Format",
                Detail = fileException.Message,
                Instance = context.Request.Path
            },
            PayloadTooLargeException payloadException => new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "Payload Too Large",
                Detail = payloadException.Message,
                Instance = context.Request.Path
            },
            NotFoundException notFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource Not Found",
                Detail = notFoundException.Message,
                Instance = context.Request.Path
            },
            ConflictException conflictException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = conflictException.Message,
                Instance = context.Request.Path
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred on the server.",
                Instance = context.Request.Path
            }
        };

        context.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
