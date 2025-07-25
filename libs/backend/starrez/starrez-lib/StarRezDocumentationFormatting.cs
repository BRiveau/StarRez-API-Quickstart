using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StarRez;

public class StarRezDocumentationFormatting
{
    /// <summary>
    /// Adds development and production StarRez API servers to OpenApiDocument servers
    /// </summary>
    private void _AddStarRezServers(OpenApiDocument document)
    {
        document.Servers = new List<OpenApiServer>();

        foreach (var url in StarRezConstants.starrezApiUrls)
        {
            document.Servers.Add(new OpenApiServer()
            {
                Description = url.Key,
                Url = url.Value
            });
        }
    }

    private void _AddStarRezAuth(OpenApiDocument document)
    {
        var basicAuth = new OpenApiSecurityScheme
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
        };

        document.Components.SecuritySchemes.Add(basicAuth.Scheme, basicAuth);
        document.SecurityRequirements.Add(new OpenApiSecurityRequirement {
              {
                  basicAuth,
                  new List<string>()
              }});
    }


    /// <summary>
    /// Adds path parameters that were missing from original Swagger documentation
    /// </summary>
    private void _FixMissingPathParameters(OpenApiDocument document)
    {
        var missingParamPaths = document.Paths.Keys.Where(apiPath =>
                apiPath.Contains("getreport") || apiPath.Contains("query")).ToList();
        foreach (var apiPath in missingParamPaths)
        {
            if (apiPath.Contains("getreport"))
            {
                var updatedName = $"{apiPath}.{{format}}";
                document.Paths.Add(updatedName, document.Paths[apiPath]);
                document.Paths.Remove(apiPath);

                foreach (var operation in document.Paths[updatedName].Operations)
                {
                    operation.Value.Parameters.Add(new OpenApiParameter()
                    {
                        Name = "format",
                        In = ParameterLocation.Path,
                        Required = true,
                        Schema = new OpenApiSchema()
                        {
                            Type = "string"
                        }
                    });
                }
            }
            else if (apiPath.Contains("query"))
            {
                document.Paths[apiPath].Operations[OperationType.Post].Description = "Allows the user to select ad-hoc data from the database using StarRez Query Language (StarQL). The data will be returned in a format appropriate for the desired content accept type.";
                var baseOperationConfig = document.Paths[apiPath].Operations[OperationType.Post];
                document.Paths[apiPath].Operations[OperationType.Post] = new OpenApiOperation(baseOperationConfig)
                {
                    RequestBody = new OpenApiRequestBody()
                    {
                        Description = "StarQL query to execute",
                        Content = new Dictionary<string, OpenApiMediaType> { { "text/plain", new OpenApiMediaType() } },
                        Required = true
                    }
                };

                document.Paths[apiPath].Operations[OperationType.Get] = new OpenApiOperation(baseOperationConfig)
                {
                    Parameters = [new OpenApiParameter()
                    {
                        Name = "q",
                        In = ParameterLocation.Query,
                        Required = true,
                        Schema = new OpenApiSchema()
                        {
                            Type = "string",
                            Description = "StarQL query to execute"
                        }
                    }]
                };
            }
        }
    }

    /// <summary>
    /// Sets default `Accept` header to specify JSON as default response type
    /// </summary>
    private void _AddAcceptJsonHeader(OpenApiOperation operation)
    {
        operation.Parameters.Add(new OpenApiParameter()
        {
            Name = "Accept",
            In = ParameterLocation.Header,
            Schema = new OpenApiSchema()
            {
                Type = "string",
                Default = new Microsoft.OpenApi.Any.OpenApiString("application/json"),
                Description = "Specifies the response return type following the MIME standard as specified by RFC 6838, section 4, for example `application/json` (for dynamic response typing, use `*/*`)",
            }
        });
    }

    /// <summary>
    /// Updates paths to fix improper path variable naming
    /// </summary>
    private void _UpdatePaths(OpenApiDocument document)
    {
        this._FixMissingPathParameters(document);
        var pathsToUpdate = document.Paths.Keys.Where(apiPath => apiPath.Contains("tablename")).ToList();

        foreach (var apiPath in pathsToUpdate)
        {
            var updatedName = apiPath.Replace("tablename", "tableName");
            document.Paths.Add(updatedName, document.Paths[apiPath]);
            document.Paths.Remove(apiPath);
        }

        this._AddAcceptJsonHeader(document);
    }

    private Dictionary<string, OpenApiMediaType> _ConstructResponseDefinition(string responseType, OpenApiMediaType responseDefinition)
    {
        var responses = new Dictionary<string, OpenApiMediaType>();

        if (apiPath.Contains("query"))
        {
            responses.Add("application/json", new OpenApiMediaType()
            {
                Schema = new OpenApiSchema()
                {
                    Description = "StarQL response data",
                    Type = "object"
                }
            });
        }

        return responses;
    }

    private void _UpdateResponseCodes(OpenApiOperation operation, string apiPath)
    {
        operation.Responses = new OpenApiResponses();

        operation.Responses.Add("200", new OpenApiResponse()
        {
            Description = "Everything went fine.",
            Content = this._ConstructResponseDefinition(apiPath)
        });
        operation.Responses.Add("400", new OpenApiResponse()
        {
            Description = "Your request is invalid, or badly formed, and we'll return an error message that tells you why.",
            Content = this._ConstructResponseDefinition("application/json", StarRezConstants.errorResponseDefinition)
        });
        operation.Responses.Add("403", new OpenApiResponse()
        {
            Description = "Your request is valid, but you do not have permission to select data from the specified table, or update the specified field.",
            Content = this._ConstructResponseDefinition("application/json", StarRezConstants.errorResponseDefinition)
        });
        operation.Responses.Add("404", new OpenApiResponse()
        {
            Description = "Your request is valid, but no data was found, or the table you are trying to use does not exist.",
            Content = this._ConstructResponseDefinition("application/json", StarRezConstants.errorResponseDefinition)
        });
    }


    /// <summary>
    /// Replaces default HTTP methods with corrected methods
    /// </summary>
    private void _UpdateHttpMethods(IDictionary<OperationType, OpenApiOperation> operations, string apiPath)
    {
        var operationData = operations[OperationType.Post];

        if ((apiPath.Contains("databaseinfo") ||
                apiPath.Contains("attachment") ||
                apiPath.Contains("select") ||
                (apiPath.Contains("photo") &&
                    !apiPath.Contains("set")) ||
                apiPath.Contains("test") ||
                apiPath.Contains("get")) &&
                operations.All(operation => operation.Value.RequestBody == null))
        {
            operations.Add(OperationType.Get, operationData);
            operations.Remove(OperationType.Post);
        }
        else if (apiPath.Contains("delete"))
        {
            operations.Add(OperationType.Delete, operationData);
            operations.Remove(OperationType.Post);
        }
        else if (apiPath.Contains("update") &&
                !apiPath.Contains("post"))
        {
            operations.Add(OperationType.Patch, operationData);
            operations.Add(OperationType.Put, operationData);
            operations.Remove(OperationType.Post);
        }
        else if (apiPath.Contains("query"))
        {
            operations.Add(OperationType.Get, operationData);
        }

        // this._UpdatePaths(document);
    }

    /// <summary>
    /// Adds enum values to parameter schemas to improve documentation quality
    /// </summary>
    private void _AddParameterEnums(IList<OpenApiParameter> parameters)
    {
        // this._UpdateResponseCodes(operation.Value, path.Key);
        int formatParameterIndex = parameters.ToList().FindIndex(parameter => parameter.Name == "format");
        if (formatParameterIndex != -1)
        {
            parameters[formatParameterIndex].Schema.Enum = StarRezConstants.starrezFormatList.ToList();
            parameters[formatParameterIndex].Schema.Default = new OpenApiString("xml");
        }
    }

    public void GenerateSpecialPropertyDescription(OpenApiSchema propertySchema, string modelName, string propertyName)
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
    /// Adds StarRez models to the OpenAPI documentation
    /// </summary>
    private async Task _AddStarRezModels(OpenApiDocument document, bool? dev)
    {
        var models = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(
                await this.GetStarRezModelDefinitions(dev));

        var formattedModels = new Dictionary<string, OpenApiSchema>();

        foreach (var model in models ?? [])
        {
            var modelName = model.Key;
            var schema = new OpenApiSchema();

            this._FixSchemaFormatting(model.Value, schema);

            if (model.Value.TryGetValue("properties", out var modelProperties) &&
                modelProperties is JObject properties)
            {
                foreach (var property in properties)
                {
                    var propertyName = property.Key;
                    var propertySchema = new OpenApiSchema();

                    this._FixSchemaFormatting(
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
    }

    /// <summary>
    /// Adds and updates the OpenAPI document provided by StarRez to improve the quality of documentation for the StarRez API
    /// </summary>
    public async Task ImproveStarRezDocumentation(OpenApiDocument document, bool? dev)
    {
        this._AddStarRezServers(document);
        this._AddStarRezAuth(document);

        foreach (var path in document.Paths)
        {
            var apiPath = path.Key;
            this._UpdateHttpMethods(path.Value.Operations, apiPath);
            foreach (var operation in path.Value.Operations)
            {
                if (!apiPath.Contains("{format}"))
                {
                    this._AddAcceptJsonHeader(operation.Value);
                }
                this._AddParameterEnums(operation.Value.Parameters);
            }
        }

        await this._AddStarRezModels(document, dev);  // To improve documentation load times, comment this line out
    }
}
