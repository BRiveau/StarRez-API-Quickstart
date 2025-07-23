using System.Net.Http.Headers;
using System.Text;
using Microsoft.OpenApi.Models;
using System.Net.Mime;

namespace StarRez;

public class StarRezClient
{
    private Dictionary<string, string> starrezApiUrls = new Dictionary<string, string>();
    private string apiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "";
    private HttpClient client;

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
    /// Updates paths to fix improper path variable naming
    /// </summary>
    private void _UpdatePaths(OpenApiDocument document)
    {
        var pathsToUpdate = document.Paths.Keys.Where(apiPath => apiPath.Contains("tablename")).ToList();

        foreach (var apiPath in pathsToUpdate)
        {
            var updatedName = apiPath.Replace("tablename", "tableName");
            document.Paths.Add(updatedName, document.Paths[apiPath]);
            document.Paths.Remove(apiPath);
        }
    }

    /// <summary>
    /// Replaces default HTTP methods with corrected methods
    /// </summary>
    public void CorrectHttpMethods(OpenApiDocument document)
    {
        this._UpdatePaths(document);

        foreach (var path in document.Paths)
        {
            if ((path.Key.Contains("databaseinfo") ||
                    path.Key.Contains("attachment") ||
                    path.Key.Contains("select") ||
                    (path.Key.Contains("photo") &&
                        !path.Key.Contains("set")) ||
                    path.Key.Contains("test") ||
                    path.Key.Contains("get")) &&
                    path.Value.Operations.All(operation => operation.Value.RequestBody == null))
            {
                var pathData = path.Value.Operations[OperationType.Post];
                path.Value.Operations.Remove(OperationType.Post);
                path.Value.Operations.Add(OperationType.Get, pathData);
            }
            else if (path.Key.Contains("delete"))
            {
                var pathData = path.Value.Operations[OperationType.Post];
                path.Value.Operations.Remove(OperationType.Post);
                path.Value.Operations.Add(OperationType.Delete, pathData);
            }
            else if (path.Key.Contains("update"))
            {
                var pathData = path.Value.Operations[OperationType.Post];
                path.Value.Operations.Remove(OperationType.Post);
                path.Value.Operations.Add(OperationType.Patch, pathData);
                path.Value.Operations.Add(OperationType.Put, pathData);
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
