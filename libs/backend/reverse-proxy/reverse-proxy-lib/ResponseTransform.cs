using System.Text;
using System.Net;
using Yarp.ReverseProxy.Transforms;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Writers;

namespace ReverseProxy;

public class ResponseTransform
{
    private readonly ResponseTransformContext _responseContext;
    private Stream? _responseBodyStream;

    public ResponseTransform(ResponseTransformContext context)
    {
        this._responseContext = context;
        this._responseBodyStream = this._responseContext.ProxyResponse?.Content.ReadAsStreamAsync().Result;
    }

    private async Task _WriteResponse(string bodyContent)
    {
        if (this._responseContext.ProxyResponse?.StatusCode != HttpStatusCode.NoContent)
        {
            this._responseContext.SuppressResponseBody = true;
            var bytes = Encoding.UTF8.GetBytes(bodyContent);

            if (this._responseContext.HttpContext.Request.Headers.ContainsKey("frontend")
                         && bool.Parse(this._responseContext.HttpContext.Request.Headers["frontend"].ToString()))
            {
                bytes = Encoding.UTF8.GetBytes($")]}}',\n{bodyContent}");
            }

            var authString = this._responseContext.HttpContext.Request.Headers.Authorization.ToString();
            if (authString.Contains("Bearer") || authString.Contains("Basic"))
            {
                bytes = Encoding.UTF8.GetBytes(bodyContent);
            }
            this._responseContext.HttpContext.Response.ContentLength = bytes.Length;
            await this._responseContext.HttpContext.Response.Body.WriteAsync(bytes);
        }
    }

    public async Task SetGatewayDocumentationServer()
    {
        if (this._responseBodyStream is null)
        {
            return;
        }

        var reader = new OpenApiStreamReader();
        var document = reader.Read(this._responseBodyStream, out var diagnostic);

        // Only conduct modification if not StarRez API documentation
        document!.Servers = new List<OpenApiServer>();
        var appendPath = this._responseContext.HttpContext.Request.Path.ToString().Split('/')[1];
        document.Servers.Add(new OpenApiServer()
        {
            Url = $"{Environment.GetEnvironmentVariable("API_URL") ?? ""}/{appendPath}"
        });

        var sb = new StringBuilder();
        var writer = new OpenApiJsonWriter(new StringWriter(sb));
        document.SerializeAsV3(writer);
        writer.Flush();
        string updatedDocument = sb.ToString();

        await this._WriteResponse(updatedDocument);
    }

    public async Task AddXssiProtection()
    {
        if (this._responseBodyStream is null)
        {
            return;
        }

        using var reader = new StreamReader(this._responseBodyStream);
        var responseBody = await reader.ReadToEndAsync();
        await this._WriteResponse(responseBody);
    }
}
