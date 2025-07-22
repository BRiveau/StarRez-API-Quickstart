using System.Net.Mime;
using System.Text;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Linq;

namespace ApiDocumentation;

public class ApiDocumentationGenerator
{
    private HttpClient client;

    public ApiDocumentationGenerator(HttpClient client)
    {
        this.client = client;
    }

    /// <summary>
    /// Converts Swagger API documentation into OpenAPI documentation format
    /// </summary>
    public async Task<Stream> ConvertSwaggerToOpenApi(string swaggerResponse)
    {
        var requestBody = new StringContent(swaggerResponse, UnicodeEncoding.UTF8, MediaTypeNames.Application.Json);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://converter.swagger.io/api/convert")
        {
            Content = requestBody,
        };
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>
    /// Ensures that all schema properties are properly set (fixes problems with JSON deserialization)
    /// </summary>
    public void FixSchemaFormatting(JObject schemaValue, OpenApiSchema schema)
    {
        if (schemaValue.TryGetValue("type", out var type))
        {
            schema.Type = type.Value<string>();
        }

        if (schemaValue.TryGetValue("format", out var format))
        {
            schema.Format = format.Value<string>();
        }

        if (schemaValue.TryGetValue("maxLength", out var maxLength))
        {
            schema.MaxLength = maxLength.Value<int>();
        }

        if (schemaValue.TryGetValue("nullable", out var nullable))
        {
            schema.Nullable = nullable.Value<bool>();
        }

        if (schemaValue.TryGetValue("description", out var description))
        {
            schema.Description = description.Value<string>();
        }

        if (schemaValue.TryGetValue("required", out var required) && required is JArray)
        {
            schema.Required = ((JArray)required).Select<JToken, string>(item =>
                item.Value<string>() ?? ""
            ).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet();
        }

        if (schemaValue.TryGetValue("enum", out var enumToken) &&
enumToken is JArray)
        {
            schema.Enum = ((JArray)enumToken).Select<JToken, IOpenApiAny>(item =>
             item.Type switch
                {
                    JTokenType.Integer => new OpenApiInteger((int)item.Value<long>()),
                    JTokenType.String => new OpenApiString(item.Value<string>()),
                    _ => new OpenApiString(item.ToString())
                }).Cast<IOpenApiAny>().ToList();
        }

        if (schemaValue.TryGetValue("oneOf", out var typesToken) &&
        typesToken is JArray)
        {
            schema.OneOf = ((JArray)typesToken)
                .Select(item =>
                        new OpenApiSchema()
                        {
                            Type = item["type"]?.Value<string>() ?? ""
                        })
            .ToList();
        }
    }
}
