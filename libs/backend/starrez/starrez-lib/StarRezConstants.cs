using System.Xml;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace StarRez;

public static class StarRezConstants
{
    public static readonly Dictionary<string, string> starrezApiUrls = new Dictionary<string, string>() {
        { "Development", $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}Dev/services" },
        { "Production", $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}/services" }
    };
    public static readonly XmlReaderSettings xmlReaderSettings = new XmlReaderSettings
    {
        Async = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        IgnoreComments = true
    };
    public static readonly IOpenApiAny[] starrezFormatList = [
                                    new OpenApiString("atom"),
                                    new OpenApiString("csv"),
                                    new OpenApiString("htm"),
                                    new OpenApiString("html"),
                                    new OpenApiString("html-xml"),
                                    new OpenApiString("json"),
                                    new OpenApiString("xml")];
    public static readonly OpenApiMediaType errorResponseDefinition = new OpenApiMediaType()
    {
        Schema = new OpenApiSchema()
        {
            Description = "Response returned from StarRez API",
            Type = "array",
            Nullable = true,
            Items = new OpenApiSchema()
            {
                Description = "StarRez HTTP error message",
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema> {
                        {
                            "description",
                            new OpenApiSchema() {
                                Description = "Description of error",
                                Type = "string"
                            }
                        }
                    }
            }
        }
    };
}
