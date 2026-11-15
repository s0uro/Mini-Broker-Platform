using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MiniBroker.Domain.Common;

namespace MiniBroker.Api;

/// <summary>
/// Maps known exceptions to ProblemDetails responses. Anything else falls through to a generic 500.
/// </summary>
internal sealed class KnownExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        (int Status, string Title, string Detail)? mapped = exception switch
        {
            DomainException e => (StatusCodes.Status400BadRequest, "Invalid request", e.Message),
            BadHttpRequestException e => (e.StatusCode, "Bad request", "The request body or parameters are invalid."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict",
                "The account was modified by another request. Please retry."),
            _ => null,
        };

        if (mapped is not { } m)
            return false;

        context.Response.StatusCode = m.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = m.Status, Title = m.Title, Detail = m.Detail },
        });
    }
}
