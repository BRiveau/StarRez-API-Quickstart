using System.Net.Http.Headers;
using System.Text;
using ApiDocumentation;

namespace StarRez;

public class StarRezClient
{
    private string starrezApiUrl = $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}/services";
    private string starrezDevApiUrl = $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}Dev/services";
    private HttpClient starrezClient;
    private ApiDocumentationGenerator apiDocumentationGenerator;

    public StarRezClient(HttpClient client)
    {
        this.starrezClient = client;
        apiDocumentationGenerator = new ApiDocumentationGenerator(this.starrezClient);

        // Ensure that we are requesting json responses
        this.starrezClient.DefaultRequestHeaders.Remove("Accept");
        this.starrezClient.DefaultRequestHeaders.Add("Accept", "application/json");

        /*
        // Configures auth header when using a single API user/key and custom authentication logic
         this.starrezClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
         Convert.ToBase64String(
         Encoding.ASCII.GetBytes(
         $"{Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? ""}:{Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? ""}"
         )
         )
         );
         */
    }



    /// <summary>
    /// Allows for reassignment of the auth header used to make StarRez API requests
    /// </summary>
    public void UpdateAuthHeader(string user, string apiKey)
    {
        starrezClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
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
        var request = new HttpRequestMessage(HttpMethod.Get, $"{(dev ? this.starrezDevApiUrl : this.starrezApiUrl).Replace("/services", "")}/swagger");
        var response = await this.starrezClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await this.apiDocumentationGenerator.ConvertSwaggerToOpenApi(await response.Content.ReadAsStringAsync());
    }

    /*
        public object MakeRequest(HttpMethod method, string endpointPath)
        {

        }
        */
}
