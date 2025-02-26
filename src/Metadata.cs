using DotNext;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json.Serialization;
using static src.Invoke;

namespace src;
public class Metadata(ILogger<Metadata> logger, Config cfg)
{
    private readonly ILogger<Metadata> _logger = logger;
    private readonly string _hostname = cfg.WebsiteHostname;

    [Function("Metadata")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/openid-configuration")] HttpRequestData req, CancellationToken ct)
    {
        var result = await TryRun(req, ct);
        if (!result.EnsureSuccess(out var response))
        {
            _logger.LogError(result.Error, "Failed to run Metadata");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
        return response;
    }

    public async Task<Result<HttpResponseData>> TryRun(HttpRequestData req, CancellationToken ct)
    {
        var responseResult = Try(() => req.CreateResponse(HttpStatusCode.OK));
        if (!responseResult.EnsureSuccess(out var response))
            return await responseResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to create response", ct);

        var metadataResult = Try(() => new MetadataResponse
        {
            Issuer = $"https://{_hostname}",
            JwksUri = $"https://{_hostname}/jwks",
        });
        if (!metadataResult.EnsureSuccess(out var metadata))
            return await metadataResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to create metadata", ct);

        var writeResult = await Try(() => response.WriteAsJsonAsync(metadata, ct));
        if (!writeResult.EnsureSuccess())
            return await writeResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to write response", ct);

        return response;
    }

    private class MetadataResponse
    {
        [JsonPropertyName("issuer")]
        public required string Issuer { get; set; }

        [JsonPropertyName("jwks_uri")]
        public required string JwksUri { get; set; }
    }
}

