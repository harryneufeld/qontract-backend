using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Qontract.API.Controllers;

/// <summary>
/// Controller-side DTO validation, single uniform style (issue #39).
///
/// Every mutation endpoint validates its input through the DI-registered
/// FluentValidation validator (registered by <c>AddValidatorsFromAssembly</c>
/// in Program.cs) and rejects invalid payloads with 400 and a
/// <c>{ errors: { field: [messages] } }</c> body — the same shape the
/// Flutter app's <c>_extractFieldError</c> already parses.
/// </summary>
public static class DtoValidation
{
    /// <summary>
    /// Returns a 400 result when <paramref name="dto"/> fails validation,
    /// otherwise null so the action can proceed.
    /// </summary>
    public static async Task<BadRequestObjectResult?> RejectIfInvalidAsync<T>(
        IValidator<T> validator, T dto, CancellationToken cancellationToken = default)
    {
        var result = await validator.ValidateAsync(dto, cancellationToken);
        if (result.IsValid)
        {
            return null;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return new BadRequestObjectResult(new { errors });
    }
}
