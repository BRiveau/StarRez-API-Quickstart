using Yarp.ReverseProxy.Transforms;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Yarp.ReverseProxy.Configuration;
using System.Text;

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
  .LoadFromConfig(reverseProxyConfig)
    .LoadFromMemory([new RouteConfig(){
            RouteId = "starrezApi",
            ClusterId= "StarRez",
            Match = new RouteMatch {
            Path = "/starrez/{*params}",
            Methods =  ["GET", "HEAD", "PUT", "POST", "PATCH", "DELETE"]
            },
            Transforms = [new Dictionary<string,string>{
            {
            "PathRemovePrefix", "/starrez"
            }
            }]
            }], [new ClusterConfig(){
            ClusterId = "StarRez",
            Destinations =
            new Dictionary<string,DestinationConfig> {{"development", new DestinationConfig(){
            Address = $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}Dev/services"
            }}, {"production", new DestinationConfig(){
            Address = $"{Environment.GetEnvironmentVariable("STARREZ_API_URL") ?? ""}/services"
            }}}
            }])
    .AddTransforms(builderContext =>
      {
          string routeId = builderContext.Route.RouteId;

          builderContext.AddRequestTransform(async requestContext =>
              {
                  ReverseProxy.RequestTransform requestTransform = new ReverseProxy.RequestTransform(requestContext);

                  // Properly handle requests made to the scalar proxy URL
                  if (routeId == "scalarProxy")
                  {
                      await requestTransform.ScalarProxy();
                  }
                  /* Only uncomment if implementing micro-service architecture and has proper authentication and authorization
                  // Adds specified StarRez API authentication information to requests
                  else if (routeId == "starrezApi")
                  {
                      requestTransform.AddStarrezAuth();
                  }
                  */
                  // Only log request body if HTTP method can contain a body (produces errors if this is not followed)
                  else if (requestContext.HttpContext.Request.Method != "GET"
                      && requestContext.HttpContext.Request.Method != "DELETE")
                  {
                      await requestTransform.LogRequestBody();
                  }
              });
          builderContext.AddResponseTransform(async responseContext =>
             {
                 ReverseProxy.ResponseTransform responseTransform = new ReverseProxy.ResponseTransform(responseContext);

                 // Only customize OpenAPI documentation servers if using custom internal APIs
                 if (routeId.Contains("Documentation") && routeId != "starrezDocumentation")
                 {
                     await responseTransform.SetGatewayDocumentationServer();
                 }
                 else
                 {
                     // XSSI protection should only be added when receiving API data on frontend
                     await responseTransform.AddXssiProtection();
                 }
             });
      });

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
    options.AddDocument("v1", "StarRez API", $"/starrez/documentation");
    foreach (var cluster in clusterConfig)
    {
        var clusterName = cluster.Key;
        var clusterDestinations = cluster.GetChildren().ToArray()[0].GetChildren();
        foreach (var destination in clusterDestinations)
        {
            if (clusterName.Contains("API"))
            {
                options.AddDocument("v1", clusterName, $"{clusterName.Split(' ')[0].ToLower()}/docs"); // Ensure that there is a mapping between OpenAPI documentation and /docs for all API clusters
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
        var authorization = request.Headers.Authorization.ToString().Split(' ');
        var authorizationType = authorization[0];
        var authorizationData = string.Join(' ', authorization.Skip(1));
        var requestHeaders = request.Headers.ToDictionary();
        bool isDev = requestHeaders.ContainsKey("dev") && bool.Parse(requestHeaders["dev"]!);
        var userClaims = context.User.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);

        var userEmail = "Unauthenticated User";

        // Add code here to update user email (for logs) based on the data within the authentication method
        if (authorizationType == "Basic")
        {
            userEmail = Encoding.ASCII.GetString(Convert.FromBase64String(authorizationData)).Split(':')[0];
        }

        // Request Logging
        Log.ForContext("Custom", true).Information($"{userEmail} made {request.Method} request to {request.Path}{queryString} {(isDev ? "(DEV)" : String.Empty)}");

        // Properly route to development or production API
        if (isDev && (proxyFeature.Route.Cluster?.Destinations.ContainsKey("development") ?? false))
        {
            proxyFeature.AvailableDestinations = proxyFeature.Route.Cluster.Destinations["development"];
        }
        else if (!isDev && (proxyFeature.Route.Cluster?.Destinations.ContainsKey("production") ?? false))
        {
            proxyFeature.AvailableDestinations = proxyFeature.Route.Cluster.Destinations["production"];
        }

        await next();

        var response = context.Response;

        // Response Logging
        Log.ForContext("Custom", true).Information($"{request.Method} request to {request.Path}{queryString} {(isDev ? "(DEV) " : "")}by {userEmail} received response of {response.StatusCode}");
    });
});

await app.RunAsync();
