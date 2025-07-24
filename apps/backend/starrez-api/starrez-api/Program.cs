using System.ComponentModel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Writers;
using Scalar.AspNetCore;
using System.Text;
using Newtonsoft.Json;
using System.Xml;
using StarRez;
using ApiDocumentation;
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
HttpClientHandler handler = new HttpClientHandler();
if ((Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "") == "Development")
{
    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
}
HttpClient client = new HttpClient(handler);
// client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKeyValidator.GetGatewayApiKey());

StarRezClient starrezApiClient = new StarRezClient(client);
ApiDocumentationGenerator apiDocumentationGenerator = new ApiDocumentationGenerator(client);

app.MapGet("/documentation",
    [ProducesResponseType(200)]
async (HttpContext context, [FromHeader] bool? dev) =>
{
    OpenApiStreamReader reader = new OpenApiStreamReader();
    OpenApiDocument document = reader.Read(
        await apiDocumentationGenerator.ConvertSwaggerToOpenApi(
            await starrezApiClient.GetStarRezDocumentation(dev)
        ), out var diagnostic);

    starrezApiClient.AddStarRezServers(document);
    starrezApiClient.CorrectHttpMethods(document);
    starrezApiClient.AddParameterEnums(document);

    var models = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(
        await starrezApiClient.GetStarRezModels(dev));

    var formattedModels = new Dictionary<string, OpenApiSchema>();

    foreach (var model in models ?? [])
    {
        var modelName = model.Key;
        var schema = new OpenApiSchema();

        apiDocumentationGenerator.FixSchemaFormatting(model.Value, schema);

        if (model.Value.TryGetValue("properties", out var modelProperties) &&
            modelProperties is JObject properties)
        {
            foreach (var property in properties)
            {
                var propertyName = property.Key;
                var propertySchema = new OpenApiSchema();

                apiDocumentationGenerator.FixSchemaFormatting(
                    property.Value as JObject ?? new JObject(), propertySchema);

                schema.Properties[propertyName] = propertySchema;
            }
        }

        formattedModels[modelName] = schema;
    }

    formattedModels = formattedModels.OrderBy(key => key.Key).ToDictionary();

    document.Components = new OpenApiComponents()
    {
        Schemas = formattedModels,
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

    var models = new Dictionary<string, OpenApiSchema>();

    var sb = new StringBuilder();
    var writer = new OpenApiJsonWriter(new StringWriter(sb));

    var enumValues = new Dictionary<string, StarRezEnum[]>();

    sb.Append("{");
    using (XmlReader tableReader = XmlReader.Create(
                await starrezApiClient.GetStarRezTables(dev),
                xmlReaderSettings))
    {
        await tableReader.MoveToContentAsync();
        while (await tableReader.ReadAsync())
        {
            var modelName = tableReader.Name;
            if (models.ContainsKey(modelName) ||
                    modelName == "Tables")
            {
                continue;
            }

            sb.Append($"\"{modelName}\":");
            models.Add(modelName, new OpenApiSchema());
            models[modelName].Type = "object";

            using (XmlReader columnReader = XmlReader.Create(
                        await starrezApiClient.GetStarRezTableAttributes(
                            modelName, dev),
                        xmlReaderSettings))
            {
                await columnReader.MoveToContentAsync();
                while (await columnReader.ReadAsync())
                {
                    if (models[modelName].Properties.ContainsKey(columnReader.Name) ||
                            modelName == columnReader.Name)
                    {
                        continue;
                    }

                    string propertyName = columnReader.Name;
                    models[modelName].Properties.Add(propertyName, new OpenApiSchema());

                    string enumName = propertyName.Substring(columnReader.Name.IndexOf('_') + 1);
                    if (columnReader.HasAttributes)
                    {
                        starrezApiClient.GenerateSpecialPropertyDescription(
                                models[modelName].Properties[propertyName],
                                modelName,
                                propertyName);

                        for (int i = 0; i < columnReader.AttributeCount; i++)
                        {
                            columnReader.MoveToAttribute(i);
                            if (columnReader.Name == "required" && bool.Parse(columnReader.Value))
                            {
                                models[modelName].Required.Add(propertyName);
                            }
                            else if (columnReader.Name == "type")
                            {
                                starrezApiClient.ImproveModelProperties(
                                        models[modelName].Properties[propertyName],
                                        propertyName,
                                        columnReader.Value.ToLower());
                            }
                            else if (columnReader.Name == "size"
                                    && int.Parse(columnReader.Value) > 0)
                            {
                                models[modelName].Properties[propertyName].MaxLength = int.Parse(columnReader.Value);
                            }
                            else if (columnReader.Name == "allowNull")
                            {
                                models[modelName].Properties[propertyName].Nullable = bool.Parse(columnReader.Value);
                                if (models[modelName].Properties[propertyName].Enum.Count > 0)
                                {
                                    models[modelName].Properties[propertyName].Enum.Add(null);
                                }
                            }
                        }

                        if (!(enumName == "LockedUserReasonEnum" ||
                          enumName.Contains("OneTimeCode")) &&
                            models[modelName].Properties[propertyName].Enum.Count < 1 &&
                            enumName.Contains("Enum"))
                        {
                            models[modelName].Properties[propertyName].Type = null;
                            models[modelName].Properties[propertyName].OneOf.Add(new OpenApiSchema()
                            {
                                Type = "integer"
                            });
                            models[modelName].Properties[propertyName].OneOf.Add(new OpenApiSchema()
                            {
                                Type = "string"
                            });

                            if (!enumValues.ContainsKey(enumName))
                            {
                                enumValues.Add(
                                        enumName, await System.Text.Json.JsonSerializer.DeserializeAsync<StarRezEnum[]>(
                                            await starrezApiClient.GetStarRezEnum(enumName, dev)) ?? []);
                            }

                            foreach (var enumValue in enumValues[enumName])
                            {
                                models[modelName].Properties[propertyName].Enum.Add(new OpenApiInteger(enumValue.enumId));
                                models[modelName].Properties[propertyName].Enum.Add(new OpenApiString(enumValue.description.Replace(" ", "")));
                            }
                        }

                        columnReader.MoveToElement();
                    }
                }
            }
            models[modelName].SerializeAsV3(writer);
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

await app.RunAsync();
