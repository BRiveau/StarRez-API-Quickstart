using System.Text;
using System.Net.Http.Headers;
using System.Web;
using Yarp.ReverseProxy.Transforms;
using System.Text.Json;
using Serilog;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Http;

namespace ReverseProxy;

public class RequestTransform
{
    private readonly RequestTransformContext _requestContext;
    private readonly string _authorization;
    private Dictionary<string, StringValues> _queryParams;
    private readonly Dictionary<string, string> _userClaims;
    private readonly Dictionary<string, string> _requestCookies;
    private readonly Dictionary<string, StringValues> _requestHeaders;
    private Stream? _requestBodyStream;
    private string _apiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "";
    private HttpClientHandler _handler = new HttpClientHandler();
    private readonly HttpClient _client;

    private void _ConstructBody(string bodyContent)
    {
        var contentBytes = Encoding.UTF8.GetBytes(bodyContent);
        this._requestContext.HttpContext.Request.Body = new MemoryStream(contentBytes);
        this._requestContext.ProxyRequest.Content!.Headers.ContentLength = contentBytes.Length;
    }

    public async Task LogRequestBody()
    {
        StreamReader reader = new StreamReader(this._requestBodyStream!);
        var requestBody = await reader.ReadToEndAsync();
        if (requestBody.Trim() != "")
        {
            Log.ForContext("Custom", true).Information($"Request Body: {requestBody}");
        }

        this._ConstructBody(requestBody);
    }

    public RequestTransform(RequestTransformContext requestContext)
    {
        this._requestContext = requestContext;
        this._requestBodyStream = this._requestContext.HttpContext!.Request.Body;
        this._queryParams = this._requestContext.HttpContext.Request.Query.ToDictionary();
        this._userClaims = this._requestContext.HttpContext.User.Claims.ToDictionary(c => c.Type, c => c.Value);
        this._requestHeaders = this._requestContext.HttpContext.Request.Headers.ToDictionary();
        this._authorization = this._requestContext.HttpContext.Request.Headers.Authorization.ToString();
        this._requestCookies = this._requestContext.HttpContext.Request.Cookies.ToDictionary();

        if ((Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "") == "Development")
        {
            this._handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        }
        this._client = new(this._handler) { BaseAddress = new Uri(this._apiUrl) };
        if (this._requestHeaders.ContainsKey("dev"))
        {
            this._client.DefaultRequestHeaders.Remove("dev");
            this._client.DefaultRequestHeaders.Add("dev", this._requestHeaders["dev"]!.ToString());
        }
    }

    public async Task ScalarProxy()
    {
        var decodedQueryString = HttpUtility.UrlDecode(this._queryParams["scalar_url"].ToString() ?? "");

        var gatewayUrl = (Environment.GetEnvironmentVariable("API_URL") ?? "").Split('/')[2].Split(':')[0];
        var clusterUrl = string.Join("/", (decodedQueryString ?? "").Split('/').Take(3));

        if (clusterUrl.Contains(gatewayUrl))
        {
            var routePath = $"/{string.Join("/", (decodedQueryString ?? "").Split('/').Skip(3))}";
            switch (clusterUrl)
            {
                // Specify custom route configurations per route here
                case "https://localhost:7050":
                    {
                        routePath = $"/starrez{routePath}";
                        break;
                    }
            }

            string? queryString = null;
            string[] splitQueryString = routePath.Split('?');
            if (splitQueryString.Length > 0)
            {
                queryString = string.Join("", splitQueryString?.Skip(1) ?? []);
            }

            this._requestContext.HttpContext.Request.QueryString = new QueryString($"?{queryString}");
            this._requestContext.Path = (splitQueryString ?? [])[0];
        }
        else if (clusterUrl == "https://uri.starrezhousing.com")
        {
            this._requestContext.Path = $"/starrez/{decodedQueryString?.Split("services")[1]}";
        }

        if (this._requestHeaders.ContainsKey("Authorization"))
        {
            var authorization = this._requestHeaders["Authorization"].ToString()?.Split(' ');
            var authorizationType = (authorization ?? [])[0];
            var authorizationKey = string.Join("", (authorization ?? []).Skip(1));
            this._requestContext.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue(authorizationType, authorizationKey);
        }

        if (this._requestContext.HttpContext.Request.Method != "GET")
        {
            await this.LogRequestBody();
        }
    }

    /// <summary>
    /// This method adds StarRez basic authentication to a request using the StarRez API information within the current environment variables.
    /// This should only be used if implementing a proper microservice architecture and there is proper authentication and authorization
    /// </summary>
    public void AddStarrezAuth()
    {
        this._requestContext.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue(
        "Basic",
        Convert.ToBase64String(
          Encoding.ASCII.GetBytes(
            $"{Environment.GetEnvironmentVariable("STARREZ_API_USER") ?? ""}:{Environment.GetEnvironmentVariable("STARREZ_API_KEY") ?? ""}"
          )
        )
        );
    }
}
