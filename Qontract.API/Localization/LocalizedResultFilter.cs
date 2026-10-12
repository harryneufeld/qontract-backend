using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Qontract.API.Localization;

/// <summary>Translate only application-owned response messages and validation
/// errors. Successful entity data, enum values, field names and codes are untouched.</summary>
public sealed class LocalizedResultFilter(IOptions<JsonOptions> options) : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (!UserMessages.German) return;
        if (context.Result is ObjectResult { Value: not null } result)
        {
            var node = JsonSerializer.SerializeToNode(result.Value, options.Value.JsonSerializerOptions);
            if (node is JsonObject obj && (result.StatusCode >= 400 || obj.Count == 1 && obj.ContainsKey("message")))
            {
                foreach (var key in new[] { "message", "error", "title" })
                    if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = UserMessages.Translate(text);
                if (obj["errors"] is JsonObject errors)
                    foreach (var key in errors.Select(e => e.Key).ToArray())
                        if (errors[key] is JsonArray messages)
                            for (var i = 0; i < messages.Count; i++)
                                if (messages[i] is JsonValue value && value.TryGetValue<string>(out var text)) messages[i] = UserMessages.Translate(text);
                result.Value = obj;
            }
        }
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
