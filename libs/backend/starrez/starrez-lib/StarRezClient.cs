using System.Net.Http.Headers;
using System.Text;
using System.Net.Mime;
using System.Xml;

namespace StarRez;

public class StarRezClient
{
    private Dictionary<string, string> starrezApiUrls = new Dictionary<string, string>();
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
    }

    /// <summary>
    /// Allows for reassignment of the auth header used to make StarRez API requests
    /// </summary>
    public void UpdateAuthHeader(HttpRequestMessage request, string user, string apiKey)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(
                  Encoding.ASCII.GetBytes(
                    $"{user}:{apiKey}"
              )
            )
        );
    }

    public void AcceptJson(HttpRequestMessage request)
    {
        request.Headers.Remove("Accept");
        request.Headers.Add("Accept", "application/json");
    }

    /// <summary>
    /// Gets StarRez API documentation and converts it into OpenAPI format
    /// </summary>
    public async Task<string> GetStarRezDocumentation(bool? dev)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{this.starrezApiUrls[(dev ?? false ? "Development" : "Production")].Replace("/services", "")}/swagger");
        this.UpdateAuthHeader(request, Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? "", Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? "");
        this.AcceptJson(request);
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
        this.UpdateAuthHeader(request, Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? "", Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? "");
        this.AcceptJson(request);
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
        this.UpdateAuthHeader(request, Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? "", Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? "");
        this.AcceptJson(request);
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
        this.UpdateAuthHeader(request, Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? "", Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? "");
        this.AcceptJson(request);
        var response = await this.client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    public async Task<HttpResponseMessage> MakeRequest(HttpRequestMessage request)
    {
        this.UpdateAuthHeader(request, Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? "", Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? "");
        this.AcceptJson(request);
        return await this.client.SendAsync(request);
    }
}
