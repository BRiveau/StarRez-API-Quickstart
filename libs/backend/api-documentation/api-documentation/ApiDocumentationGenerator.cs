using System.Net.Mime;
using System.Text;

namespace ApiDocumentation;

public class ApiDocumentationGenerator
{
    private HttpClient client;

    public ApiDocumentationGenerator(HttpClient client)
    {
        this.client = client;
    }

    /// <summary>
    /// Converts Swagger API documentation into OpenAPI documentation format
    /// </summary>
    public async Task<Stream> ConvertSwaggerToOpenApi(string swaggerResponse)
    {
        var requestBody = new StringContent(swaggerResponse, UnicodeEncoding.UTF8, MediaTypeNames.Application.Json);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://converter.swagger.io/api/convert")
        {
            Content = requestBody,
        };
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }
}
