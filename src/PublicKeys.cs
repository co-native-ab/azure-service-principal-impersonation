using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using DotNext;

using static src.Invoke;
using System.Text.Json.Serialization;

namespace src;
public class PublicKeys
{
    private readonly ILogger<PublicKeys> _logger;
    private readonly KeyClient _client;
    private readonly string _vaultOpenIDConnectJwks;

    public PublicKeys(ILogger<PublicKeys> logger, Config cfg)
    {
        TryCreate(logger, cfg).ThrowGuard(out var publicKeys);
        _logger = publicKeys._logger;
        _client = publicKeys._client;
        _vaultOpenIDConnectJwks = publicKeys._vaultOpenIDConnectJwks;
    }

    private PublicKeys(ILogger<PublicKeys> logger, KeyClient client, string vaultOpenIDConnectJwks)
    {
        _logger = logger;
        _client = client;
        _vaultOpenIDConnectJwks = vaultOpenIDConnectJwks;
    }

    private static Result<PublicKeys> TryCreate(ILogger<PublicKeys> logger, Config cfg)
    {
        var keyVaultUrlResult = Try(() => new Uri(cfg.KeyVaultUrl));
        if (!keyVaultUrlResult.EnsureSuccess(out var keyVaultUrl))
            return keyVaultUrlResult.FromError<Uri, PublicKeys>("Failed to parse KeyVaultUrl");

        var credentialOptionsResult = Try(() => new DefaultAzureCredentialOptions());
        if (!credentialOptionsResult.EnsureSuccess(out var credentialOptions))
            return credentialOptionsResult.FromError<DefaultAzureCredentialOptions, PublicKeys>("Failed to create DefaultAzureCredentialOptions");

        if (cfg.KeyVaultClientId != null)
            credentialOptions.ManagedIdentityClientId = cfg.KeyVaultClientId;

        var credentialResult = Try(() => new DefaultAzureCredential(credentialOptions));
        if (!credentialResult.EnsureSuccess(out var credential))
            return credentialResult.FromError<DefaultAzureCredential, PublicKeys>("Failed to create DefaultAzureCredential");

        var keyClientResult = Try(() => new KeyClient(keyVaultUrl, credential));
        if (!keyClientResult.EnsureSuccess(out var keyClient))
            return keyClientResult.FromError<KeyClient, PublicKeys>("Failed to create KeyClient");

        var publicKeysResult = Try(() => new PublicKeys(logger, keyClient, cfg.KeyVaultOpenIDConnectJwks));
        if (!publicKeysResult.EnsureSuccess(out var publicKeys))
            return publicKeysResult.FromError("Failed to create PublicKeys");

        return publicKeys;
    }


    [Function("PublicKeys")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jwks")] HttpRequestData req, CancellationToken ct)
    {
        var result = await TryRun(req, ct);
        if (!result.EnsureSuccess(out var response))
        {
            _logger.LogError(result.Error, "Failed to run PublicKeys");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
        return response;
    }

    public async Task<Result<HttpResponseData>> TryRun(HttpRequestData req, CancellationToken ct)
    {
        var keyVersionsResult = Try(() => _client.GetPropertiesOfKeyVersionsAsync(_vaultOpenIDConnectJwks, ct));
        if (!keyVersionsResult.EnsureSuccess(out var keyVersions))
            return await keyVersionsResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to get key versions", ct);

        var jwksResult = Try(() => new List<Jwk>());
        if (!jwksResult.EnsureSuccess(out var jwks))
            return await jwksResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to create jwks", ct);

        await foreach (var keyVersionResult in TryEnumerable(() => keyVersions))
        {
            if (!keyVersionResult.EnsureSuccess(out var keyVersion))
                return await keyVersionResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to get key version", ct);

            if (keyVersion == null)
                continue;

            if (keyVersion.Enabled == false)
                continue;

            var keyResponseResult = await Try(() => _client.GetKeyAsync(_vaultOpenIDConnectJwks, keyVersion.Version, ct));
            if (!keyResponseResult.EnsureSuccess(out var keyResponse))
                return await keyResponseResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to get key", ct);

            if (keyResponse.Value.KeyType == KeyType.Rsa || keyResponse.Value.KeyType == KeyType.RsaHsm)
            {
                var rsaPublicKeyModulusResult = Try(() => Base64UrlEncoder.Encode(keyResponse.Value.Key.N));
                if (!rsaPublicKeyModulusResult.EnsureSuccess(out var rsaPublicKeyModulus))
                    return await rsaPublicKeyModulusResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to encode RSA public key modulus (N)", ct);

                var rsaPublicKeyExponentResult = Try(() => Base64UrlEncoder.Encode(keyResponse.Value.Key.E));
                if (!rsaPublicKeyExponentResult.EnsureSuccess(out var rsaPublicKeyExponent))
                    return await rsaPublicKeyExponentResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to encode RSA public key exponent (E)", ct);

                var addJwksResult = Try(() => jwks.Add(new Jwk
                {
                    KeyType = "RSA",
                    PublicKeyUse = "sig",
                    KeyId = keyVersion.Version,
                    RsaPublicKeyModulus = rsaPublicKeyModulus,
                    RsaPublicKeyExponent = rsaPublicKeyExponent,
                }));
                if (!addJwksResult.EnsureSuccess())
                    return await addJwksResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to add key to jwks", ct);
            }
        }

        var responseResult = Try(() => req.CreateResponse(HttpStatusCode.OK));
        if (!responseResult.EnsureSuccess(out var response))
            return await responseResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to create response", ct);

        var writeResult = await Try(() => response.WriteAsJsonAsync(new JwksResponse(jwks), ct));
        if (!writeResult.EnsureSuccess())
            return await writeResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "Failed to write response", ct);

        return response;
    }

    private class Jwk
    {
        [JsonPropertyName("kty")]
        public required string KeyType { get; set; }

        [JsonPropertyName("use")]
        public required string PublicKeyUse { get; set; }

        [JsonPropertyName("kid")]
        public required string KeyId { get; set; }

        [JsonPropertyName("n")]
        public required string RsaPublicKeyModulus { get; set; }

        [JsonPropertyName("e")]
        public required string RsaPublicKeyExponent { get; set; }
    }

    private class JwksResponse(List<Jwk> keys)
    {
        [JsonPropertyName("keys")]
        public List<Jwk> Jwks => keys;
    }
}

