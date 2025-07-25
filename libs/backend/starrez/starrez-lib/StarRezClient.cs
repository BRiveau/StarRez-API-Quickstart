using System.Net.Http.Headers;
using System.Text;
using Microsoft.OpenApi.Models;
using System.Net.Mime;
using System.Xml;
using Microsoft.OpenApi.Any;

namespace StarRez;

public class StarRezClient
{
    private Dictionary<string, string> starrezApiUrls = new Dictionary<string, string>();
    private string apiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "";
    private HttpClient client;
    private XmlReaderSettings xmlReaderSettings = new XmlReaderSettings
    {
        Async = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        IgnoreComments = true
    };


    public StarRezClient(HttpClient client)
    {
        this.client = client;

        // Add production and development API URLs
        this.starrezApiUrls.Add("Production", $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}/services");
        this.starrezApiUrls.Add("Development", $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}Dev/services");

        // Ensure that we are requesting json responses
        this.client.DefaultRequestHeaders.Remove("Accept");
        this.client.DefaultRequestHeaders.Add("Accept", "application/json");

        // Configures default auth header for StarRez documentation requests
        this.UpdateAuthHeader(Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? "", Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? "");
    }

    /// <summary>
    /// Allows for reassignment of the auth header used to make StarRez API requests
    /// </summary>
    public void UpdateAuthHeader(string user, string apiKey)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(
                  Encoding.ASCII.GetBytes(
                    $"{user}:{apiKey}"
              )
            )
        );
    }

    /// <summary>
    /// Gets StarRez API documentation and converts it into OpenAPI format
    /// </summary>
    public async Task<string> GetStarRezDocumentation(bool? dev)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{this.starrezApiUrls[(dev ?? false ? "Development" : "Production")].Replace("/services", "")}/swagger");
        var response = await this.client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Adds development and production StarRez API servers to OpenApiDocument servers
    /// </summary>
    public void AddStarRezServers(OpenApiDocument document)
    {
        document.Servers = new List<OpenApiServer>();

        foreach (var url in this.starrezApiUrls)
        {
            document.Servers.Add(new OpenApiServer()
            {
                Description = url.Key,
                Url = url.Value
            });
        }
    }

    /// <summary>
    /// Adds path parameters that were missing from original Swagger documentation
    /// </summary>
    private void _FixMissingPathParameters(OpenApiDocument document)
    {
        var missingParamPaths = document.Paths.Keys.Where(apiPath => apiPath.Contains("getreport") || apiPath.Contains("query")).ToList();
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
    private void _AddAcceptJsonHeader(OpenApiDocument document)
    {
        var pathsToUpdate = document.Paths.Keys.Where(apiPath => !apiPath.Contains("{format}")).ToList();

        foreach (var apiPath in pathsToUpdate)
        {
            foreach (var operation in document.Paths[apiPath].Operations.Keys)
            {
                document.Paths[apiPath].Operations[operation].Parameters.Add(new OpenApiParameter()
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
        }
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


    /// <summary>
    /// Replaces default HTTP methods with corrected methods
    /// </summary>
    public void CorrectHttpMethods(OpenApiDocument document)
    {
        foreach (var path in document.Paths)
        {
            var operationData = path.Value.Operations[OperationType.Post];

            if ((path.Key.Contains("databaseinfo") ||
                    path.Key.Contains("attachment") ||
                    path.Key.Contains("select") ||
                    (path.Key.Contains("photo") &&
                        !path.Key.Contains("set")) ||
                    path.Key.Contains("test") ||
                    path.Key.Contains("get")) &&
                    path.Value.Operations.All(operation => operation.Value.RequestBody == null))
            {
                path.Value.Operations.Add(OperationType.Get, operationData);
                path.Value.Operations.Remove(OperationType.Post);
            }
            else if (path.Key.Contains("delete"))
            {
                path.Value.Operations.Add(OperationType.Delete, operationData);
                path.Value.Operations.Remove(OperationType.Post);
            }
            else if (path.Key.Contains("update") &&
                    !path.Key.Contains("post"))
            {
                path.Value.Operations.Add(OperationType.Patch, operationData);
                path.Value.Operations.Add(OperationType.Put, operationData);
                path.Value.Operations.Remove(OperationType.Post);
            }
            else if (path.Key.Contains("query"))
            {
                path.Value.Operations.Add(OperationType.Get, operationData);
            }
        }

        this._UpdatePaths(document);
    }

    private Dictionary<string, OpenApiMediaType> _ConstructErrorResponses()
    {
        var errorResponses = new Dictionary<string, OpenApiMediaType>();
        errorResponses.Add("application/json", new OpenApiMediaType()
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
        });
        return errorResponses;
    }

    private Dictionary<string, OpenApiMediaType> _GetProperResponseType(string apiPath)
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
            Content = this._GetProperResponseType(apiPath)
        });
        operation.Responses.Add("400", new OpenApiResponse()
        {
            Description = "Your request is invalid, or badly formed, and we'll return an error message that tells you why.",
            Content = this._ConstructErrorResponses()
        });
        operation.Responses.Add("403", new OpenApiResponse()
        {
            Description = "Your request is valid, but you do not have permission to select data from the specified table, or update the specified field.",
            Content = this._ConstructErrorResponses()
        });
        operation.Responses.Add("404", new OpenApiResponse()
        {
            Description = "Your request is valid, but no data was found, or the table you are trying to use does not exist.",
            Content = this._ConstructErrorResponses()
        });
    }

    /// <summary>
    /// Adds enum values to parameter schemas to improve documentation quality
    /// </summary>
    public void AddParameterEnums(OpenApiDocument document)
    {
        foreach (var path in document.Paths)
        {
            foreach (var operation in path.Value.Operations)
            {
                this._UpdateResponseCodes(operation.Value, path.Key);
                for (int i = 0; i < operation.Value.Parameters.Count; i++)
                {
                    var parameterData = document
                        .Paths[path.Key]
                        .Operations[operation.Key]
                        .Parameters[i];

                    switch (operation.Value.Parameters[i].Name)
                    {
                        case "format":
                            {
                                IOpenApiAny[] formatList = [
                                    new OpenApiString("atom"),
                                    new OpenApiString("csv"),
                                    new OpenApiString("htm"),
                                    new OpenApiString("html"),
                                    new OpenApiString("html-xml"),
                                    new OpenApiString("json"),
                                    new OpenApiString("xml")];

                                parameterData
                                    .Schema
                                    .Enum = formatList.ToList();
                                break;
                            }
                    }
                }
            }
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
    /// Makes an HTTP request to get all StarRez model definitions
    /// </summary>
    public async Task<string> GetStarRezModels(bool? dev)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/starrez/models");
        request.Headers.Add("dev", (dev ?? false).ToString());
        var response = await this.client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Makes an HTTP request to get all StarRez tables
    /// </summary>
    public async Task<Stream> GetStarRezTables(bool? dev)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{this.starrezApiUrls[(dev ?? false ? "Development" : "Production")]}/databaseinfo/tablelist.xml");
        var response = await this.client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>
    /// Makes an HTTP request to get all StarRez attributes for the specified table
    /// </summary>
    public async Task<Stream> GetStarRezTableAttributes(string tableName, bool? dev)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{this.starrezApiUrls[(dev ?? false ? "Development" : "Production")]}/databaseinfo/columnlist/{tableName}.xml");
        var response = await this.client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>
    /// Makes an HTTP request to get all enum values available for the specified enum
    /// </summary>
    public async Task<Stream> GetStarRezEnum(string enumName, bool? dev)
    {
        var requestBody = new StringContent(
                $"SELECT {enumName} AS enumId, Description AS description FROM {enumName}",
                UnicodeEncoding.UTF8,
                MediaTypeNames.Application.Json);
        var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{this.starrezApiUrls[(dev ?? false ? "Development" : "Production")]}/query")
        {
            Content = requestBody
        };
        var response = await this.client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    /*
        public object MakeRequest(HttpMethod method, string endpointPath)
        {

        }
        */
}
