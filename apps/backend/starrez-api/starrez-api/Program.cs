using System.ComponentModel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Writers;
using Scalar.AspNetCore;
using System.Text;
using System.Xml;
using StarRez;
using ApiDocumentation;
using Microsoft.OpenApi.Any;

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
StarRezDocumentationFormatting documentationFormatting = new StarRezDocumentationFormatting(starrezApiClient);

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

    await documentationFormatting.ImproveStarRezDocumentation(document, dev);

    var sb = new StringBuilder();
    var writer = new OpenApiJsonWriter(new StringWriter(sb));
    document.SerializeAsV3(writer);
    writer.Flush();

    return Results.Text(sb.ToString(), "application/json");
}).CacheOutput()
    .WithDescription("Gets StarRez API documentation in OpenAPI format")
    .WithTags("StarRez API Documentation")
    .Stable();

await app.RunAsync();
