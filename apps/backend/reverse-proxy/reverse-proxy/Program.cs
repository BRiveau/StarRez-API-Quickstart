using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Management;
using Scalar.AspNetCore;

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

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
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

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
