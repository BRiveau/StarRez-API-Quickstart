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
    // Get StarRez Swagger API documentation
    var swaggerRequest = new HttpRequestMessage(HttpMethod.Get, $"{((dev ?? false) ? starrezDevApiUrl : starrezApiUrl).Replace("/services", "")}/swagger");
    var swaggerResponse = await starrezClient.SendAsync(swaggerRequest);

    // Convert Swagger documentation to OpenAPI documentation
    var openApiRequestBody = new StringContent(await swaggerResponse.Content.ReadAsStringAsync(), UnicodeEncoding.UTF8, MediaTypeNames.Application.Json);
    var openApiRequest = new HttpRequestMessage(HttpMethod.Post, "https://converter.swagger.io/api/convert")
    {
        Content = openApiRequestBody,
    };
    var openApiResponse = await client.SendAsync(openApiRequest);

    var reader = new OpenApiStreamReader();
    var document = reader.Read(await openApiResponse.Content.ReadAsStreamAsync(), out var diagnostic);

    // Modify document data
    document!.Servers = new List<OpenApiServer>();
    document.Servers.Add(new OpenApiServer()
    {
        Description = "Development",
        Url = starrezDevApiUrl
    });
    document.Servers.Add(new OpenApiServer()
    {
        Description = "Production",
        Url = starrezApiUrl
    });

    var modelsRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/starrez/models");
    modelsRequest.Headers.Add("dev", (dev ?? false).ToString());
    var modelsResponse = await client.SendAsync(modelsRequest);

    document.Components = new OpenApiComponents()
    {
        Schemas = JsonConvert.DeserializeObject<Dictionary<string, OpenApiSchema>>(await modelsResponse.Content.ReadAsStringAsync()),
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
}).WithDescription("Gets StarRez API documentation in OpenAPI format")
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

    sb.Append("{");
    using (XmlReader tableReader = XmlReader.Create(await tableResponse.Content.ReadAsStreamAsync(), xmlReaderSettings))
    {
        await tableReader.MoveToContentAsync();
        while (await tableReader.ReadAsync())
        {
            if (schemas.ContainsKey(tableReader.Name) || tableReader.Name == "Tables")
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
                    if (schemas[tableReader.Name].Properties.ContainsKey(columnReader.Name) || tableReader.Name == columnReader.Name)
                    {
                        continue;
                    }

                    string attributeName = columnReader.Name;
                    schemas[tableReader.Name].Properties.Add(attributeName, new OpenApiSchema());

                    if (columnReader.HasAttributes)
                    {
                        // Construct table references
                        if (attributeName == "TableID")
                        {
                            schemas[tableReader.Name].Properties[attributeName].Description = "ID of element in Table specified in TableName";
                        }
                        else if (attributeName == "TableName")
                        {
                            schemas[tableReader.Name].Properties[attributeName].Description = "Name of table to be used for reference";
                        }
                        else if (attributeName.Contains("ID") &&
                                tableReader.Name != attributeName.Replace("ID", "") &&
                                attributeName.Length != 3)
                        {
                            schemas[tableReader.Name].Properties[attributeName].Description = $"References {attributeName.Substring(attributeName.IndexOf('_') + 1).Replace("ID", "")} table";
                        }
                        else if (attributeName.Contains("ID") &&
                                attributeName.Length != 3)
                        {
                            schemas[tableReader.Name].Properties[attributeName].Description = $"Primary identification key for {attributeName.Substring(attributeName.IndexOf('_') + 1).Replace("ID", "")} table";
                        }
                        else if (attributeName.Contains("Enum"))
                        {
                            schemas[tableReader.Name].Properties[attributeName].Description = $"References {attributeName} table";
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
                                            break;
                                        }
                                    case "longstring":
                                        {
                                            goto case "binary";
                                        }
                                    case "short":
                                        {
                                            schemas[tableReader.Name].Properties[attributeName].Type = "integer";
                                            schemas[tableReader.Name].Properties[attributeName].Format = "int16";
                                            schemas[tableReader.Name].Properties[attributeName].Description = "Signed 16-bit integer";
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
                                    schemas[tableReader.Name].Properties[attributeName].Description = "An email address as defined as Mailbox by RFC5321, section 2.3.11, for example, example@domain.com";
                                }
                                else if (attributeName == "Password")
                                {
                                    Console.WriteLine("Password");
                                    schemas[tableReader.Name].Properties[attributeName].Type = "string";
                                    schemas[tableReader.Name].Properties[attributeName].Format = "byte";
                                    schemas[tableReader.Name].Properties[attributeName].Description = "Base64 encoded data as defined by RFC4648, section 6";
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
    sb.Append("}");

    return Results.Text(sb.ToString(), "application/json");
}).WithDescription("Gets StarRez API models in OpenApi component scheme format")
    .WithTags("StarRez API Documentation")
    .Stable();

await app.RunAsync();
