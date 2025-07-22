using System.ComponentModel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Writers;
using Scalar.AspNetCore;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Mime;
using Newtonsoft.Json;
using System.Xml;
using StarRez;
using Microsoft.OpenApi.Any;
using Newtonsoft.Json.Linq;

// Define auth schemes for API (the StarRez API uses Basic authentication)
OpenApiSecurityScheme[] authSchemes = [
        new OpenApiSecurityScheme
        {
            Description = "Basic authentication scheme",
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Basic"
            },
            Scheme = "Basic",
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            In = ParameterLocation.Header,
        },
];

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOutputCache();

builder.Services.AddOpenApi((options) =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new()
                {
                    Title = "StarRez Custom API",
                    Version = "v1",
                    Description = "API for customizing request/response logic when interacting with the StarRez API"
                };

                document.Components = new OpenApiComponents();
                document.Components.SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>();

                foreach (var authScheme in authSchemes)
                {
                    document.Components.SecuritySchemes.Add(authScheme.Scheme, authScheme);
                    document.SecurityRequirements.Add(new OpenApiSecurityRequirement {
              {
                  authScheme,
                  new List<string>()
              }});
                }
                return Task.CompletedTask;
            });

    options.AddScalarTransformers();
});

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.UseOutputCache();
app.UseHttpsRedirection();

// Configure HTTP clients
string apiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "";

string starrezApiUrl = $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}/services";
string starrezDevApiUrl = $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}Dev/services";
HttpClientHandler handler = new HttpClientHandler();
if ((Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "") == "Development")
{
    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
}
HttpClient client = new(handler) { BaseAddress = new Uri(apiUrl) };
// client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKeyValidator.GetGatewayApiKey());

StarRezClient starrezApiClient = new StarRezClient(client);
HttpClient starrezClient = new HttpClient { BaseAddress = new Uri(starrezApiUrl) };
starrezClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
        Convert.ToBase64String(
          Encoding.ASCII.GetBytes(
            $"{Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? ""}:{Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? ""}"
          )
        )
);
starrezClient.DefaultRequestHeaders.Add("Accept", "application/json");

app.MapGet("/documentation",
    [ProducesResponseType(200)]
async (HttpContext context, [FromHeader] bool? dev) =>
{
    var reader = new OpenApiStreamReader();
    var document = reader.Read(await starrezApiClient.GetStarRezDocumentation(), out var diagnostic);

    starrezApiClient.AddStarRezServers(document ?? new OpenApiDocument());

    var modelsRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/starrez/models");
    modelsRequest.Headers.Add("dev", (dev ?? false).ToString());
    var modelsResponse = await client.SendAsync(modelsRequest);
    modelsResponse.EnsureSuccessStatusCode();

    var modelsData = await modelsResponse.Content.ReadAsStringAsync();

    var modelSchema = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(modelsData);

    // Fix schema formatting
    var finalSchemas = new Dictionary<string, OpenApiSchema>();

    foreach (var model in modelSchema ?? [])
    {
        var modelName = model.Key;
        var modelData = model.Value;
        var schema = new OpenApiSchema();

        if (modelData.TryGetValue("type", out var typeToken))
        {
            schema.Type = typeToken.Value<string>();
        }

        if (modelData.TryGetValue("format", out var formatToken))
        {
            schema.Format = formatToken.Value<string>();
        }

        if (modelData.TryGetValue("description", out var descToken))
        {
            schema.Description = descToken.Value<string>();
        }

        if (modelData.TryGetValue("required", out var reqToken) && reqToken is JArray)
        {
            schema.Required = ((JArray)reqToken).Select<JToken, string>(item =>
                item.Value<string>() ?? ""
            ).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet();
        }


        if (modelData.TryGetValue("properties", out var modelProperties) &&
        modelProperties is JObject propertiesObject)
        {
            schema.Properties = new Dictionary<string, OpenApiSchema>();
            foreach (var property in propertiesObject)
            {
                var propertyName = property.Key;
                var propertyData = property.Value as JObject;

                var propertySchema = new OpenApiSchema();

                if (propertyData?.TryGetValue("type", out var propType) ?? false)
                {
                    propertySchema.Type = propType.Value<string>();
                }

                if (propertyData?.TryGetValue("maxLength", out var propMaxLength) ?? false)
                {
                    propertySchema.MaxLength = propMaxLength.Value<int>();
                }

                if (propertyData?.TryGetValue("nullable", out var propNullable) ?? false)
                {
                    propertySchema.Nullable = propNullable.Value<bool>();
                }

                if (propertyData?.TryGetValue("format", out var propFormat) ?? false)
                {
                    propertySchema.Format = propFormat.Value<string>();
                }

                if (propertyData?.TryGetValue("description", out var propDesc) ?? false)
                {
                    propertySchema.Description = propDesc.Value<string>();
                }

                if (propertyData?.TryGetValue("enum", out var enumToken) ?? false &&
                enumToken is JArray)
                {
                    propertySchema.Enum = ((JArray)enumToken).Select<JToken, IOpenApiAny>(item =>
                     item.Type switch
                        {
                            JTokenType.Integer => new OpenApiInteger((int)item.Value<long>()),
                            JTokenType.String => new OpenApiString(item.Value<string>()),
                            _ => new OpenApiString(item.ToString())
                        }).Cast<IOpenApiAny>().ToList();
                }

                if (propertyData?.TryGetValue("oneOf", out var typesToken) ?? false &&
                typesToken is JArray)
                {
                    propertySchema.OneOf = ((JArray)typesToken)
                        .Select(item =>
                                new OpenApiSchema()
                                {
                                    Type = item["type"]?.Value<string>() ?? ""
                                })
                    .ToList();
                }


                schema.Properties[propertyName] = propertySchema;
            }
        }

        finalSchemas[modelName] = schema;
    }

    finalSchemas = finalSchemas.OrderBy(key => key.Key).ToDictionary();

    document.Components = new OpenApiComponents()
    {
        Schemas = finalSchemas,
    };

    foreach (var authScheme in authSchemes)
    {
        document.Components.SecuritySchemes.Add(authScheme.Scheme, authScheme);
        document.SecurityRequirements.Add(new OpenApiSecurityRequirement {
              {
                  authScheme,
                  new List<string>()
              }});
    }

    var sb = new StringBuilder();
    var writer = new OpenApiJsonWriter(new StringWriter(sb));
    document.SerializeAsV3(writer);
    writer.Flush();

    return Results.Text(sb.ToString(), "application/json");
}).CacheOutput()
    .WithDescription("Gets StarRez API documentation in OpenAPI format")
    .WithTags("StarRez API Documentation")
    .Stable();

app.MapGet("/models",
    [ProducesResponseType(200)]
async ([FromHeader] bool? dev) =>
{
    XmlReaderSettings xmlReaderSettings = new XmlReaderSettings
    {
        Async = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        IgnoreComments = true
    };

    var schemas = new Dictionary<string, OpenApiSchema>();
    var tableRequest = new HttpRequestMessage(HttpMethod.Get, $"{((dev ?? false) ? starrezDevApiUrl : starrezApiUrl)}/databaseinfo/tablelist.xml");
    var tableResponse = await starrezClient.SendAsync(tableRequest);

    var sb = new StringBuilder();
    var writer = new OpenApiJsonWriter(new StringWriter(sb));

    var enumValues = new Dictionary<string, StarRezEnum[]>();

    sb.Append("{");
    using (XmlReader tableReader = XmlReader.Create(await tableResponse.Content.ReadAsStreamAsync(), xmlReaderSettings))
    {
        await tableReader.MoveToContentAsync();
        while (await tableReader.ReadAsync())
        {
            if (schemas.ContainsKey(tableReader.Name) ||
                    tableReader.Name == "Tables")
            {
                continue;
            }

            sb.Append($"\"{tableReader.Name}\":");
            List<string> requiredAttributes = new List<string>();
            schemas.Add(tableReader.Name, new OpenApiSchema());
            schemas[tableReader.Name].Type = "object";

            var modelRequest = new HttpRequestMessage(HttpMethod.Get, $"{((dev ?? false) ? starrezDevApiUrl : starrezApiUrl)}/databaseinfo/columnlist/{tableReader.Name}.xml");
            var modelResponse = await starrezClient.SendAsync(modelRequest);

            using (XmlReader columnReader = XmlReader.Create(await modelResponse.Content.ReadAsStreamAsync(), xmlReaderSettings))
            {
                await columnReader.MoveToContentAsync();
                while (await columnReader.ReadAsync())
                {
                    if (schemas[tableReader.Name].Properties.ContainsKey(columnReader.Name) ||
                            tableReader.Name == columnReader.Name)
                    {
                        continue;
                    }

                    string attributeName = columnReader.Name;
                    schemas[tableReader.Name].Properties.Add(attributeName, new OpenApiSchema());

                    string enumName = attributeName.Substring(columnReader.Name.IndexOf('_') + 1);
                    if (columnReader.HasAttributes)
                    {
                        // Construct table references
                        if (attributeName == "TableID")
                        {
                            schemas[tableReader.Name].Properties[attributeName].Description = "ID of element in specified Table";
                        }
                        else if (attributeName == "TableName")
                        {
                            schemas[tableReader.Name].Properties[attributeName].Description = "Name of table to be used for reference";
                        }
                        else if (attributeName.Length != 3 &&
                                !attributeName.Contains("GUID") &&
                                attributeName.Contains("ID") &&
                                tableReader.Name != attributeName.Replace("ID", ""))
                        {
                            schemas[tableReader.Name].Properties[attributeName].Description = $"References {attributeName.Substring(attributeName.IndexOf('_') + 1).Replace("ID", "")} table";
                        }
                        else if (attributeName.Length != 3 &&
                                !attributeName.Contains("GUID") &&
                                attributeName.Contains("ID"))
                        {
                            schemas[tableReader.Name].Properties[attributeName].Description = $"Primary identification key for {attributeName.Substring(attributeName.IndexOf('_') + 1).Replace("ID", "")} table";
                        }

                        for (int i = 0; i < columnReader.AttributeCount; i++)
                        {
                            columnReader.MoveToAttribute(i);
                            if (columnReader.Name == "required" && bool.Parse(columnReader.Value))
                            {
                                schemas[tableReader.Name].Required.Add(attributeName);
                            }
                            else if (columnReader.Name == "type")
                            {
                                switch (columnReader.Value.ToLower())
                                {
                                    case string dateType when dateType.Contains("datetime"):
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = "string";
                                            schemas[tableReader.Name].Properties[attributeName].Format = "date-time";
                                            schemas[tableReader.Name].Properties[attributeName].Description = "Date and time as defined by RFC 3339, section 5.6, for example, 2017-07-21T17:32:28Z";
                                            break;
                                        }
                                    case "date":
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = "string";
                                            schemas[tableReader.Name].Properties[attributeName].Format = "date";
                                            schemas[tableReader.Name].Properties[attributeName].Description = "Date as defined by RFC 3339, section 5.6, for example, 2017-07-21";
                                            break;
                                        }
                                    case "timestamp":
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = "string";
                                            schemas[tableReader.Name].Properties[attributeName].Format = "time";
                                            schemas[tableReader.Name].Properties[attributeName].Description = "Time as defined by RFC 3339, section 5.6, for example, 17:32:28Z";
                                            break;
                                        }
                                    case "money":
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = "number";
                                            schemas[tableReader.Name].Properties[attributeName].Format = "decimal";
                                            schemas[tableReader.Name].Properties[attributeName].Description = "A fixed point decimal number of unspecified precision and range";
                                            break;
                                        }
                                    case "decimal":
                                        {
                                            goto case "money";
                                        }
                                    case "bigdecimal":
                                        {
                                            goto case "money";
                                        }
                                    case "binary":
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = "string";
                                            schemas[tableReader.Name].Properties[attributeName].Format = "binary";
                                            schemas[tableReader.Name].Properties[attributeName].Description = "Any sequence of octets";
                                            break;
                                        }
                                    case "byte":
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = "string";
                                            schemas[tableReader.Name].Properties[attributeName].Format = "byte";
                                            schemas[tableReader.Name].Properties[attributeName].Description = "Base64 encoded data as defined by RFC4648, section 6";
                                            break;
                                        }
                                    case "guid":
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = "string";
                                            schemas[tableReader.Name].Properties[attributeName].Format = "guid";
                                            schemas[tableReader.Name].Properties[attributeName].Description = "A Globally Unique Identifier, defined as UUID by RFC 9562, section 4, for example, f81d4fae-7dec-11d0-a765-00a0c91e6bf6";
                                            break;
                                        }
                                    case "longstring":
                                        {
                                            goto case "binary";
                                        }
                                    case "short":
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = "integer";
                                            schemas[tableReader.Name].Properties[attributeName].Format = "uint16";
                                            schemas[tableReader.Name].Properties[attributeName].Description = "Unsigned 16-bit integer";
                                            break;
                                        }
                                    default:
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = columnReader.Value.ToLower();
                                            break;
                                        }
                                }

                                if (attributeName.Contains("Email") || attributeName == "PortalAuthProviderUserID")
                                {
                                    schemas[tableReader.Name].Properties[attributeName].Type = "string";
                                    schemas[tableReader.Name].Properties[attributeName].Format = "email";
                                    schemas[tableReader.Name].Properties[attributeName].Description = "An email address, defined as Mailbox by RFC5321, section 2.3.11, for example, example@domain.com";
                                }
                            }
                            else if (columnReader.Name == "size"
                                    && int.Parse(columnReader.Value) > 0)
                            {
                                schemas[tableReader.Name].Properties[attributeName].MaxLength = int.Parse(columnReader.Value);
                            }
                            else if (columnReader.Name == "allowNull")
                            {
                                schemas[tableReader.Name].Properties[attributeName].Nullable = bool.Parse(columnReader.Value);
                                if (schemas[tableReader.Name].Properties[attributeName].Enum.Count > 0)
                                {
                                    schemas[tableReader.Name].Properties[attributeName].Enum.Add(null);
                                }
                            }
                        }

                        if (!(enumName == "LockedUserReasonEnum" ||
                          enumName.Contains("OneTimeCode")) &&
                            schemas[tableReader.Name].Properties[attributeName].Enum.Count < 1 &&
                            enumName.Contains("Enum"))
                        {
                            schemas[tableReader.Name].Properties[attributeName].Type = null;
                            schemas[tableReader.Name].Properties[attributeName].OneOf.Add(new OpenApiSchema()
                            {
                                Type = "integer"
                            });
                            schemas[tableReader.Name].Properties[attributeName].OneOf.Add(new OpenApiSchema()
                            {
                                Type = "string"
                            });

                            if (!enumValues.ContainsKey(enumName))
                            {
                                var enumRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/starrez/models/{enumName}");
                                enumRequest.Headers.Add("dev", (dev ?? false).ToString());
                                var enumResponse = await client.SendAsync(enumRequest);
                                enumResponse.EnsureSuccessStatusCode();
                                enumValues.Add(enumName, await System.Text.Json.JsonSerializer.DeserializeAsync<StarRezEnum[]>(await enumResponse.Content.ReadAsStreamAsync()) ?? []);
                            }

                            foreach (var enumValue in enumValues[enumName])
                            {
                                schemas[tableReader.Name].Properties[attributeName].Enum.Add(new OpenApiInteger(enumValue.enumId));
                                schemas[tableReader.Name].Properties[attributeName].Enum.Add(new OpenApiString(enumValue.description.Replace(" ", "")));
                            }
                        }

                        columnReader.MoveToElement();
                    }
                }
            }
            schemas[tableReader.Name].SerializeAsV3(writer);
            writer.Flush();

            sb.Append(",");
        }
    }

    sb.Remove(sb.Length - 1, 1);
    sb.Append("\n}");

    return Results.Text(sb.ToString(), "application/json");
}).CacheOutput()
    .WithDescription("Gets StarRez API models in OpenApi component scheme format")
    .WithTags("StarRez API Documentation")
    .Stable();

app.MapGet("/models/{enumType}",
    [ProducesResponseType<StarRezEnum[]>(200)]
async ([FromHeader] bool? dev,
    string enumType) =>
{
    var enumRequestBody = new StringContent($"SELECT {enumType} AS enumId, Description AS description FROM {enumType}", UnicodeEncoding.UTF8, MediaTypeNames.Application.Json);
    var enumRequest = new HttpRequestMessage(HttpMethod.Post, $"{((dev ?? false) ? starrezDevApiUrl : starrezApiUrl)}/query")
    {
        Content = enumRequestBody
    };
    var enumResponse = await starrezClient.SendAsync(enumRequest);
    enumResponse.EnsureSuccessStatusCode();
    return Results.Ok(await System.Text.Json.JsonSerializer.DeserializeAsync<StarRezEnum[]>(await enumResponse.Content.ReadAsStreamAsync()));
}).CacheOutput()
    .WithDescription("Gets specified StarRez Enum information")
    .WithTags("StarRez API Documentation")
    .Stable();

await app.RunAsync();
