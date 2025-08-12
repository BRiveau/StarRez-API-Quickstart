using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StarRez;

public class StarRezDocumentationFormatting
{
    private StarRezModelFormatting _starRezModelFormatter;

    public StarRezDocumentationFormatting(StarRezClient client)
    {
        this._starRezModelFormatter = new StarRezModelFormatting(client);
    }

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

    /// <summary>
    /// Adds Basic Authentication as the default auth scheme in the OpenAPI document
    /// </summary>
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

        if (!document.Components.SecuritySchemes.ContainsKey(basicAuth.Scheme))
        {
            document.Components.SecuritySchemes.Add(basicAuth.Scheme, basicAuth);
        }
        document.SecurityRequirements.Add(new OpenApiSecurityRequirement {
              {
                  basicAuth,
                  new List<string>()
              }});
    }

    /// <summary>
    /// Adds format parameter to endpoints that utilize it, but it is not specified in documentation
    /// </summary>>
    private void _FixFormatParameter(OpenApiPaths paths, string existingPath)
    {

        var updatedPath = $"{existingPath}.{{format}}";
        paths.Add(updatedPath, paths[existingPath]);
        paths.Remove(existingPath);

        foreach (var operation in paths[updatedPath].Operations)
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

    /// <summary>
    /// Updates paths to fix improper path variable naming
    /// </summary>
    private void _FixTableNameParameter(OpenApiPaths paths, string existingPath)
    {
        var updatedName = existingPath.Replace("tablename", "tableName");
        paths.Add(updatedName, paths[existingPath]);
        paths.Remove(existingPath);
    }

    /// <summary>
    /// Fixes all path parameter problems in the OpenAPI document
    /// </summary>
    private void _FixPathParameters(OpenApiDocument document)
    {
        var pathsToUpdate = document.Paths.Keys.Where(apiPath =>
                apiPath.Contains("tablename")
                || apiPath.Contains("getreport")).ToList();

        foreach (var apiPath in pathsToUpdate)
        {
            switch (apiPath)
            {
                case string path when path.Contains("tablename"):
                    {
                        this._FixTableNameParameter(document.Paths, apiPath);
                        break;
                    }
                case string path when path.Contains("getreport"):
                    {
                        this._FixFormatParameter(document.Paths, apiPath);
                        break;
                    }
            }
        }
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
    }

    /// <summary>
    /// Properly documents both `query` endpoints from StarRez API
    /// </summary>
    private void _FixQueryParameters(IDictionary<OperationType, OpenApiOperation> operations)
    {
        operations[OperationType.Post].Description = "Allows the user to select ad-hoc data from the database using StarRez Query Language (StarQL). The data will be returned in a format appropriate for the desired content accept type.";
        var baseOperationConfig = operations[OperationType.Post];
        operations[OperationType.Post] = new OpenApiOperation(baseOperationConfig)
        {
            RequestBody = new OpenApiRequestBody()
            {
                Description = "StarQL query to execute",
                Content = new Dictionary<string, OpenApiMediaType> { { "text/plain", new OpenApiMediaType() } },
                Required = true
            }
        };

        operations[OperationType.Get] = new OpenApiOperation(baseOperationConfig)
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

    /// <summary>
    /// Simplifies process of constructing response definitions
    /// </summary>
    private Dictionary<string, OpenApiMediaType> _ConstructResponseDefinition(string responseType, OpenApiMediaType responseDefinition)
    {
        return new Dictionary<string, OpenApiMediaType>(){
            {
                responseType,
                responseDefinition
            }
        };
    }

    /// <summary>
    /// Constructs the proper response definition for the specified endpoint
    /// </summary>
    private Dictionary<string, OpenApiMediaType> _GetEndpointResponse(string apiPath)
    {
        switch (apiPath)
        {
            case string path when path.Contains("query"):
                {
                    return this._ConstructResponseDefinition("application/json", new OpenApiMediaType()
                    {
                        Schema = new OpenApiSchema()
                        {
                            Description = "StarQL response data",
                            Type = "object"
                        }
                    });
                }
            default:
                {
                    return new Dictionary<string, OpenApiMediaType>();
                }
        }
    }

    /// <summary>
    /// Updates HTTP response codes to reflect StarRez documentation, as well as provide a better idea of return types
    /// </summary>
    private void _UpdateResponseCodes(OpenApiOperation operation, string apiPath)
    {
        operation.Responses = new OpenApiResponses();

        operation.Responses.Add("200", new OpenApiResponse()
        {
            Description = "Everything went fine.",
            Content = this._GetEndpointResponse(apiPath)
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
    /// Adds StarRez models to the OpenAPI documentation
    /// </summary>
    private async Task _AddStarRezModels(OpenApiDocument document, bool? dev)
    {
        var models = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(
                await this._starRezModelFormatter.CreateStarRezModelsSchema(dev));
        document.Components.Schemas = this._starRezModelFormatter.FormatStarRezModelsSchema(models ?? new Dictionary<string, JObject>());
    }

    /// <summary>
    /// Adds and updates the OpenAPI document provided by StarRez to improve the quality of documentation for the StarRez API
    /// </summary>
    public async Task ImproveStarRezDocumentation(OpenApiDocument document, bool? dev)
    {
        this._AddStarRezServers(document);
        this._AddStarRezAuth(document);
        this._FixPathParameters(document);

        foreach (var path in document.Paths)
        {
            var apiPath = path.Key;
            this._UpdateHttpMethods(path.Value.Operations, apiPath);

            if (apiPath.Contains("query"))
            {
                this._FixQueryParameters(path.Value.Operations);
            }

            foreach (var operation in path.Value.Operations)
            {
                if (!apiPath.Contains("{format}"))
                {
                    this._AddAcceptJsonHeader(operation.Value);
                }
                this._AddParameterEnums(operation.Value.Parameters);
                this._UpdateResponseCodes(operation.Value, apiPath);
            }
        }

        await this._AddStarRezModels(document, dev);  // To improve documentation load times, comment this line out
    }
}
