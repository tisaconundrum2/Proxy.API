using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Configure settings from configuration
builder.Services.Configure<ProxySettings>(builder.Configuration.GetSection("ProxySettings"));
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("MongoSettings"));

// Register the MongoDB client (singleton)
builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var mongoSettings = serviceProvider.GetRequiredService<IOptions<MongoSettings>>().Value;
    return new MongoClient(mongoSettings.ConnectionString);
});

// Register the MongoCacheRepository
builder.Services.AddSingleton<MongoCacheRepository>();

// Configure HttpClient with Polly (optional; adjust as needed)
builder.Services.AddHttpClient("ProxyClient", client =>
{
    var timeoutSeconds = builder.Configuration.GetValue<int>("ProxySettings:TimeoutSeconds", 5);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
})
.AddTransientHttpErrorPolicy(policyBuilder => policyBuilder
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 3,
        durationOfBreak: TimeSpan.FromSeconds(30)
    ));

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
