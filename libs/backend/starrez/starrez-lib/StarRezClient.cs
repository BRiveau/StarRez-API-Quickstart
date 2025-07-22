using System.Net.Http.Headers;
using System.Text;
using Microsoft.OpenApi.Models;
using ApiDocumentation;

namespace StarRez;

public class StarRezClient
{
    private Dictionary<string, string> starrezApiUrls = new Dictionary<string, string>();
    private string apiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "";
    private HttpClient client;
    private ApiDocumentationGenerator apiDocumentationGenerator;

    public StarRezClient(HttpClient client)
    {
        this.client = client;
        apiDocumentationGenerator = new ApiDocumentationGenerator(this.client);

        // Add production and development API URLs
        this.starrezApiUrls.Add("Production", $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}/services");
        this.starrezApiUrls.Add("Development", $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}Dev/services");

        // Ensure that we are requesting json responses
        this.client.DefaultRequestHeaders.Remove("Accept");
        this.client.DefaultRequestHeaders.Add("Accept", "application/json");

        /*
        // Configures auth header when using a single API user/key and custom authentication logic
         this.UpdateAuthHeader(Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? "", Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? "");
         */
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
    public async Task<Stream> GetStarRezDocumentation(bool dev = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{this.starrezApiUrls[(dev ? "Development" : "Production")].Replace("/services", "")}/swagger");
        var response = await this.client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await this.apiDocumentationGenerator.ConvertSwaggerToOpenApi(await response.Content.ReadAsStringAsync());
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

    public async Task<string> GetStarRezModels(bool dev = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/starrez/models");
        request.Headers.Add("dev", dev.ToString());
        var response = await this.client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /*
        public object MakeRequest(HttpMethod method, string endpointPath)
        {

        }
        */
}
