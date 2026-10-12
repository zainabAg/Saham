using System.Collections.Generic;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Saham.Infrastructure.Idempotency;

/* OpenAPI: every [Idempotent] operation documents the required Idempotency-Key header and its 409s. */
public class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.MethodInfo.IsDefined(typeof(IdempotentAttribute), inherit: true))
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = IdempotencyActionFilter.HeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Schema = new OpenApiSchema { Type = "string", MaxLength = 128 },
            Description = "One value per user action (e.g. a UUID), 1–128 printable ASCII characters. Retry with the SAME key " +
                          "and the SAME body: a completed request returns its saved response (header Idempotent-Replayed: true) " +
                          "without running again. Kept 7 days."
        });
        operation.Responses.TryAdd("400", new OpenApiResponse { Description = "Saham:IdempotencyKeyRequired — header missing or invalid." });
        operation.Responses.TryAdd("409", new OpenApiResponse
        {
            Description = "Saham:IdempotencyConflict — same key, different body.  " +
                          "Saham:RequestInProgress — the same request is still running (Retry-After: 1; retry with the same key)."
        });
    }
}
