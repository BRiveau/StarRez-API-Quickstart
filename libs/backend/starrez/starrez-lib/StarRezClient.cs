using System.Net.Http.Headers;
using System.Text;
using System.Net.Mime;
using System.Xml;

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
        this.starrezApiUrls.Add("Development", $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}Dev/services");
        this.starrezApiUrls.Add("Production", $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}/services");

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

    public async Task<string> GetStarRezModelDefinitions(bool? dev)
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
