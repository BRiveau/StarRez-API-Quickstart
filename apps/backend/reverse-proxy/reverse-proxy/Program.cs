using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Management;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Add App Settings Files (Contains reverse proxy configuration)
builder.Configuration.AddJsonFile("appsettings.json", true, true);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    true,
    true
    );

// Initial reverse proxy setup
var reverseProxyConfig = builder.Configuration.GetSection("ReverseProxy");
builder.Services.AddReverseProxy()
  .ConfigureHttpClient((context, handler) =>
  {
      handler.AutomaticDecompression = System.Net.DecompressionMethods.All;
  })
  .LoadFromConfig(reverseProxyConfig);

// Configure Logging
string logFilePath = $"../../logs/{DateTime.Now.ToString("MM-dd-yyyy")}-reverse-proxy.log"; // Sets output filename and directory
string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"; // Uses Serilog format to configure logging output styling
Log.Logger = new LoggerConfiguration()
  .Filter.ByIncludingOnly(le => (le.Level >= LogEventLevel.Warning)
      || (le.Properties.ContainsKey("Custom")
        && bool.Parse(le.Properties["Custom"].ToString())))
  .WriteTo.Console(outputTemplate: outputTemplate)
  .WriteTo.File(logFilePath,
      outputTemplate: outputTemplate)
  .CreateLogger();
builder.Services.AddSerilog();

// Configures OpenAPI documentation (More information at https://aka.ms/aspnet/openapi)
builder.Services.AddOpenApi(
        options =>
        {
            // Sets documentation website title, version, and description
            options.AddDocumentTransformer((document, context, cancellationToken) =>
                      {
                          document.Info = new()
                          {
                              Title = "Reverse Proxy",
                              Version = "v1",
                              Description = "Reverse proxy for handling interaction with all APIs (default is configured with StarRez API support)"
                          };
                          return Task.CompletedTask;
                      });

            // Allows for Scalar documentation transformer features
            options.AddScalarTransformers();
        }
        );

var app = builder.Build();

// Generates OpenAPI documentation for application
app.MapOpenApi();

// Adds API documentation for all specified clusters
var clusterConfig = builder.Configuration.GetSection("ReverseProxy:Clusters").GetChildren();
app.MapScalarApiReference("/docs", (options) =>
{
    options.ProxyUrl = $"{Environment.GetEnvironmentVariable("API_URL") ?? ""}/scalar-proxy";
    foreach (var cluster in clusterConfig)
    {
        var clusterName = cluster.Key;
        var clusterDestinations = cluster.GetChildren().ToArray()[0].GetChildren();
        foreach (var destination in clusterDestinations)
        {
            if (clusterName.Contains("API"))
            {
                options.AddDocument("v1", clusterName, $"{clusterName.Split(' ')[0].ToLower()}/docs");
                if (clusterName == "StarRez Internal API")
                {
                    options.AddDocument("v1", "StarRez Native API", $"{clusterName.Split(' ')[0].ToLower()}/native/docs");
                }
            }
        }
    }
});

app.UseHttpsRedirection();
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.Use(async (context, next) =>
    {
        var proxyFeature = context.GetReverseProxyFeature();
        var clusterId = proxyFeature.Route.Cluster?.ClusterId;
        var request = context.Request;
        var queryString = request.QueryString;
        var authorization = request.Headers.Authorization.ToString();
        var requestHeaders = request.Headers.ToDictionary();
        bool isDev = requestHeaders.ContainsKey("dev") && bool.Parse(requestHeaders["dev"]!);
        var userClaims = context.User.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);

        // Request Logging
        Log.ForContext("Custom", true).Information($"{"User"} made {request.Method} request to {request.Path}{queryString} {(isDev ? "(DEV)" : String.Empty)}");

        await next();

        var response = context.Response;

        // Response Logging
        Log.ForContext("Custom", true).Information($"{request.Method} request to {request.Path}{queryString} {(isDev ? "(DEV) " : "")}by {"User"} received response of {response.StatusCode}");
    });
});

await app.RunAsync();
