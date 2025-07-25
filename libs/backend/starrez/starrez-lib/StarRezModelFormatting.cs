using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Linq;

namespace StarRez;

public class StarRezModelFormatting
{
    private void GenerateSpecialPropertyDescription(OpenApiSchema propertySchema, string modelName, string propertyName)
    {
        if (propertyName == "TableID")
        {
            propertySchema.Description = "ID of element in specified table";
        }
        else if (propertyName == "TableName")
        {
            propertySchema.Description = "Name of table to be used for reference";
        }
        else if (propertyName.Length != 3 &&
                !propertyName.Contains("GUID") &&
                propertyName.Contains("ID") &&
                modelName != propertyName.Replace("ID", ""))
        {
            propertySchema.Description = $"References {propertyName.Substring(propertyName.IndexOf('_') + 1).Replace("ID", "")} table";
        }
        else if (propertyName.Length != 3 &&
                !propertyName.Contains("GUID") &&
                propertyName.Contains("ID"))
        {
            propertySchema.Description = $"Primary identification key for {propertyName.Substring(propertyName.IndexOf('_') + 1).Replace("ID", "")} table";
        }
    }


    /// <summary>
    /// Ensures that all schema properties are properly set (fixes problems with JSON deserialization)
    /// </summary>
    private void _FixSchemaFormatting(JObject schemaValue, OpenApiSchema schema)
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

    /// <summary>
    /// Updates property data on models to improve quality of documentation
    /// </summary>
    public void ImproveModelProperties(OpenApiSchema propertySchema, string propertyName, string existingType)
    {
        switch (existingType)
        {
            case string dateType when dateType.Contains("datetime"):
                {
                    propertySchema.Type = "string";
                    propertySchema.Format = "date-time";
                    propertySchema.Description = "Date and time as defined by RFC 3339, section 5.6, for example, 2017-07-21T17:32:28Z";
                    break;
                }
            case "date":
                {
                    propertySchema.Type = "string";
                    propertySchema.Format = "date";
                    propertySchema.Description = "Date as defined by RFC 3339, section 5.6, for example, 2017-07-21";
                    break;
                }
            case "timestamp":
                {
                    propertySchema.Type = "string";
                    propertySchema.Format = "time";
                    propertySchema.Description = "Time as defined by RFC 3339, section 5.6, for example, 17:32:28Z";
                    break;
                }
            case "money":
                {
                    propertySchema.Type = "number";
                    propertySchema.Format = "decimal";
                    propertySchema.Description = "A fixed point decimal number of unspecified precision and range";
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
                    propertySchema.Type = "string";
                    propertySchema.Format = "binary";
                    propertySchema.Description = "Any sequence of octets";
                    break;
                }
            case "byte":
                {
                    propertySchema.Type = "string";
                    propertySchema.Format = "byte";
                    propertySchema.Description = "Base64 encoded data as defined by RFC4648, section 6";
                    break;
                }
            case "guid":
                {
                    propertySchema.Type = "string";
                    propertySchema.Format = "guid";
                    propertySchema.Description = "A Globally Unique Identifier, defined as UUID by RFC 9562, section 4, for example, f81d4fae-7dec-11d0-a765-00a0c91e6bf6";
                    break;
                }
            case "longstring":
                {
                    goto case "binary";
                }
            case "short":
                {
                    propertySchema.Type = "integer";
                    propertySchema.Format = "uint16";
                    propertySchema.Description = "Unsigned 16-bit integer";
                    break;
                }
            default:
                {
                    propertySchema.Type = existingType;
                    break;
                }
        }


        if (propertyName.Contains("Email") || propertyName == "PortalAuthProviderUserID")
        {
            propertySchema.Type = "string";
            propertySchema.Format = "email";
            propertySchema.Description = "An email address, defined as Mailbox by RFC5321, section 2.3.11, for example, example@domain.com";
        }
    }
}
