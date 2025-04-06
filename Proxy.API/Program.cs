using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using RestSharp;


var builder = WebApplication.CreateBuilder(args);

// Add Environment Variables
// You can set environment variables like this:
// export ProxySettings__TimeoutSeconds=10
// export MongoSettings__ConnectionString="mongodb://localhost:27017"
builder.Configuration.AddEnvironmentVariables();

// Add controllers
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Configure settings from configuration
builder.Services.Configure<ProxySettings>(builder.Configuration.GetSection("ProxySettings"));
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("MongoSettings"));

// Register singletons for settings
builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var mongoSettings = serviceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
    return new MongoClient(mongoSettings.ConnectionString);
});
builder.Services.AddSingleton<MongoCacheRepository>();
builder.Services.AddTransient(sp => new RestClient());

// Configure HttpClient with Polly (optional; adjust as needed)
builder.Services.AddHttpClient("ProxyClient", client =>
{
    var timeoutSeconds = builder.Configuration.GetValue<int>("ProxySettings:TimeoutSeconds", 5);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
})
.AddPolicyHandler(
    HttpPolicyExtensions.HandleTransientHttpError()
        .Or<TimeoutRejectedException>()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
);

// Rate Limiting (optional)
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }
        )
    );
});

var app = builder.Build();

// Configure middleware
app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
