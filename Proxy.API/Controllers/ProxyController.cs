using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using RestSharp;
using Proxy.API.Models;
using System.Text.Json;

namespace ProxyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MongoCacheRepository _cacheRepository;
        private readonly ILogger<ProxyController> _logger;
        private readonly ProxySettings _settings;
        private readonly RestClient _restClient;

        public ProxyController(
            IHttpClientFactory httpClientFactory,
            MongoCacheRepository cacheRepository,
            ILogger<ProxyController> logger,
            IOptions<ProxySettings> settings,
            RestClient restClient)
        {
            _httpClientFactory = httpClientFactory;
            _cacheRepository = cacheRepository;
            _logger = logger;
            _settings = settings.Value;
            _restClient = restClient;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string url)
        {
            _logger.LogInformation("Received GET request for URL: {Url}", url);

            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("Missing URL parameter in request");
                return BadRequest("Missing 'url' query parameter.");
            }

            // Validate the base URL format
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri baseUri) ||
                (baseUri.Scheme != "http" && baseUri.Scheme != "https"))
            {
                _logger.LogWarning("Invalid URL format: {Url}", url);
                return BadRequest("Invalid URL format. Only HTTP and HTTPS URLs are supported.");
            }

            // Reconstruct the full URL by appending all query parameters except 'url'
            var queryParams = Request.Query
                .Where(q => !string.Equals(q.Key, "url", StringComparison.OrdinalIgnoreCase))
                .Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}");

            var separator = url.Contains("?") ? "&" : "?";
            var fullUrl = $"{url}{separator}{string.Join("&", queryParams)}";

            _logger.LogInformation("Reconstructed full URL: {FullUrl}", fullUrl);

            // Create a cache key that includes relevant headers
            var cacheKey = CreateCacheKey(fullUrl, Request.Headers);

            // Attempt to retrieve a valid cached response from MongoDB
            var cachedResponse = await _cacheRepository.GetCacheAsync(cacheKey);
            if (cachedResponse != null && cachedResponse.ExpirationTime > DateTime.UtcNow)
            {
                _logger.LogInformation("Cache hit for URL: {Url}", fullUrl);
                return Content(cachedResponse.Content, cachedResponse.ContentType);
            }

            _logger.LogInformation("Cache miss for URL: {Url}", fullUrl);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Create RestRequest
                var request = new RestRequest(fullUrl);

                // Forward all headers from the incoming request
                foreach (var header in Request.Headers)
                {
                    if (!ShouldSkipHeader(header.Key))
                    {
                        request.AddHeader(header.Key, header.Value.ToString());
                        _logger.LogDebug("Forwarding header: {HeaderKey}={HeaderValue}", header.Key, header.Value);
                    }
                }

                // Execute the request
                var response = await _restClient.ExecuteAsync(request);
                stopwatch.Stop();

                if (!response.IsSuccessful)
                {
                    _logger.LogWarning("Error response from {Url}: {StatusCode}", fullUrl, response.StatusCode);
                }

                var responseContent = response.Content ?? string.Empty;
                var contentType = response.ContentType ?? "application/json";

                // Build the cache entry with an expiration time
                var cacheEntry = new CachedResponse
                {
                    Hash = cacheKey,
                    Url = fullUrl,
                    Content = responseContent,
                    ContentType = contentType,
                    ExpirationTime = DateTime.UtcNow.AddSeconds(_settings.CacheExpirationSeconds)
                };

                // Persist the cache entry in MongoDB
                await _cacheRepository.SetCacheAsync(cacheEntry);

                _logger.LogInformation("Fetched and cached response from {Url} in {ElapsedMs}ms",
                    fullUrl, stopwatch.ElapsedMilliseconds);

                return Content(responseContent, contentType);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error fetching the URL: {Url} after {ElapsedMs}ms", fullUrl, stopwatch.ElapsedMilliseconds);

                // Fallback to cache if available
                if (cachedResponse != null && cachedResponse.ExpirationTime > DateTime.UtcNow)
                {
                    _logger.LogInformation("Returning cached response for URL: {Url}", fullUrl);
                    return Content(cachedResponse.Content, cachedResponse.ContentType);
                }

                return StatusCode(504, "The target endpoint is taking too long, and no cached response is available.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromQuery] string url, [FromBody] object content)
        {
            _logger.LogInformation("Received POST request for URL: {Url}", url);
            
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("Missing URL parameter in request");
                return BadRequest("Missing 'url' query parameter.");
            }
            
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri baseUri) ||
                (baseUri.Scheme != "http" && baseUri.Scheme != "https"))
            {
                _logger.LogWarning("Invalid URL format: {Url}", url);
                return BadRequest("Invalid URL format. Only HTTP and HTTPS URLs are supported.");
            }

            var queryParams = Request.Query
                .Where(q => !string.Equals(q.Key, "url", StringComparison.OrdinalIgnoreCase))
                .Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}");
            
            var separator = url.Contains("?") ? "&" : "?";
            var fullUrl = $"{url}{separator}{string.Join("&", queryParams)}";

            _logger.LogInformation("Reconstructed full URL: {FullUrl}", fullUrl);
            
            var hashedContent = ComputeHash(JsonSerializer.Serialize(content));
            var hashedUrl = ComputeHash(fullUrl);
            var cacheKey = CreateCacheKey($"{hashedUrl}|{hashedContent}", Request.Headers);
            var cachedResponse = await _cacheRepository.GetCacheAsync(cacheKey);
            
            if (cachedResponse != null && cachedResponse.ExpirationTime > DateTime.UtcNow)
            {
                _logger.LogInformation("Cache hit for URL: {Url}", fullUrl);
                return Content(cachedResponse.Content, cachedResponse.ContentType);
            }
            
            _logger.LogInformation("Cache miss for URL: {Url}", fullUrl);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var request = new RestRequest(fullUrl, Method.Post);
                foreach (var header in Request.Headers)
                {
                    if (!ShouldSkipHeader(header.Key))
                    {
                        request.AddHeader(header.Key, header.Value.ToString());
                        _logger.LogDebug("Forwarding header: {HeaderKey}={HeaderValue}", header.Key, header.Value);
                    }
                }
                
                request.AddJsonBody(content);

                var response = await _restClient.ExecuteAsync(request);
                stopwatch.Stop();

                if (!response.IsSuccessful)
                {
                    _logger.LogWarning("Error response from {Url}: {StatusCode}", fullUrl, response.StatusCode);
                }
                
                var responseContent = response.Content ?? string.Empty;
                var contentType = response.ContentType ?? "application/json";
                
                var cacheEntry = new CachedResponse
                {
                    Hash = cacheKey,
                    Url = fullUrl,
                    Content = responseContent,
                    ContentType = contentType,
                    ExpirationTime = DateTime.UtcNow.AddSeconds(_settings.CacheExpirationSeconds)
                };
                
                await _cacheRepository.SetCacheAsync(cacheEntry);
                
                _logger.LogInformation("Fetched and cached response from {Url} in {ElapsedMs}ms",
                    fullUrl, stopwatch.ElapsedMilliseconds);
                
                return Content(responseContent, contentType);
            }
            catch (Exception)
            {
                stopwatch.Stop();
                _logger.LogError("Error fetching the URL: {Url} after {ElapsedMs}ms", fullUrl, stopwatch.ElapsedMilliseconds);

                if (cachedResponse != null && cachedResponse.ExpirationTime > DateTime.UtcNow)
                {
                    _logger.LogInformation("Returning cached response for URL: {Url}", fullUrl);
                    return Content(cachedResponse.Content, cachedResponse.ContentType);
                }

                return StatusCode(504, "The target endpoint is taking too long, and no cached response is available.");
            }
        }

        private bool ShouldSkipHeader(string headerName)
        {

            var skipHeaders = new[]
            {
                "Host",
                "Connection",
                "Content-Length",
                "Origin",
                "Accept-Encoding"
            };

            return skipHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase);
        }

        private string CreateCacheKey(string url, IHeaderDictionary headers)
        {
            var sb = new StringBuilder(url);
            foreach (var header in headers)
            {
                if (!ShouldSkipHeader(header.Key))
                {
                    sb.Append($"|{header.Key}:{header.Value}");
                }
            }
            return ComputeHash(sb.ToString());
        }

        private string ComputeHash(string content)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
