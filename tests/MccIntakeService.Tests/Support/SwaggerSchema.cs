using System.Text.Json;

namespace MccIntakeService.Tests.Support;

/// <summary>
/// Helpers for asserting against the generated OpenAPI document. Swashbuckle expresses
/// inheritance as an <c>allOf</c> of <c>$ref</c>s, so reading a derived schema's full property
/// set means following both.
/// </summary>
internal static class SwaggerSchema
{
    /// <summary>The content map for one documented response, e.g. paths → /api/x → post → 422.</summary>
    public static JsonElement ResponseContent(JsonElement swagger, string path, string verb, string status) =>
        swagger.GetProperty("paths")
            .GetProperty(path)
            .GetProperty(verb)
            .GetProperty("responses")
            .GetProperty(status)
            .GetProperty("content");

    /// <summary>Media types a documented response can be served as.</summary>
    public static IReadOnlyList<string> MediaTypes(JsonElement content) =>
        content.EnumerateObject().Select(media => media.Name).ToList();

    /// <summary>Every property a schema exposes, following <c>$ref</c> and <c>allOf</c>.</summary>
    public static IReadOnlySet<string> PropertyNames(JsonElement swagger, JsonElement schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Collect(swagger, schema, names);

        return names;
    }

    /// <summary>Convenience overload resolving the schema of one media type on a response.</summary>
    public static IReadOnlySet<string> PropertyNamesFor(
        JsonElement swagger,
        string path,
        string verb,
        string status,
        string mediaType = "application/problem+json")
    {
        var schema = ResponseContent(swagger, path, verb, status)
            .GetProperty(mediaType)
            .GetProperty("schema");

        return PropertyNames(swagger, schema);
    }

    private static void Collect(JsonElement swagger, JsonElement schema, HashSet<string> names)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!.Split('/')[^1];

            if (swagger.GetProperty("components").GetProperty("schemas").TryGetProperty(name, out var resolved))
            {
                Collect(swagger, resolved, names);
            }
        }

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var member in allOf.EnumerateArray())
            {
                Collect(swagger, member, names);
            }
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                names.Add(property.Name);
            }
        }
    }
}
