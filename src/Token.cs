using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using DarkLoop.Azure.Functions.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Microsoft.Graph.Users.Item.CheckMemberGroups;
using DotNext;

using static src.Invoke;
using System.Text.Json.Serialization;

namespace src;
public class Token
{
    private readonly ILogger<Token> _logger;
    private readonly DefaultAzureCredential _credential;
    private readonly KeyClient _keyClient;
    private readonly string _keyVaultOpenIDConnectJwks;
    private readonly string _hostname;
    private readonly GraphServiceClient _graphClient;

    public Token(ILogger<Token> logger, Config cfg)
    {
        TryCreate(logger, cfg).ThrowGuard(out var token);
        _logger = token._logger;
        _credential = token._credential;
        _keyClient = token._keyClient;
        _keyVaultOpenIDConnectJwks = token._keyVaultOpenIDConnectJwks;
        _graphClient = token._graphClient;
        _hostname = token._hostname;
    }

    private Token(
        ILogger<Token> logger,
        KeyClient client,
        string vaultOpenIDConnectJwks,
        DefaultAzureCredential cred,
        GraphServiceClient graphClient,
        string hostname)
    {
        _logger = logger;
        _keyClient = client;
        _keyVaultOpenIDConnectJwks = vaultOpenIDConnectJwks;
        _credential = cred;
        _graphClient = graphClient;
        _hostname = hostname;
    }

    private static Result<Token> TryCreate(ILogger<Token> logger, Config cfg)
    {
        var keyVaultUrlResult = Try(() => new Uri(cfg.KeyVaultUrl));
        if (!keyVaultUrlResult.EnsureSuccess(out var keyVaultUrl))
            return keyVaultUrlResult.FromError<Uri, Token>("Failed to parse KeyVaultUrl");

        var credentialOptionsResult = Try(() => new DefaultAzureCredentialOptions());
        if (!credentialOptionsResult.EnsureSuccess(out var credentialOptions))
            return credentialOptionsResult.FromError<DefaultAzureCredentialOptions, Token>("Failed to create DefaultAzureCredentialOptions");

        if (cfg.KeyVaultClientId != null)
            credentialOptions.ManagedIdentityClientId = cfg.KeyVaultClientId;

        var credentialResult = Try(() => new DefaultAzureCredential(credentialOptions));
        if (!credentialResult.EnsureSuccess(out var credential))
            return credentialResult.FromError<DefaultAzureCredential, Token>("Failed to create DefaultAzureCredential");

        var keyClientResult = Try(() => new KeyClient(keyVaultUrl, credential));
        if (!keyClientResult.EnsureSuccess(out var keyClient))
            return keyClientResult.FromError<KeyClient, Token>("Failed to create KeyClient");

        var graphClientResult = Try(() => new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]));
        if (!graphClientResult.EnsureSuccess(out var graphClient))
            return graphClientResult.FromError<GraphServiceClient, Token>("Failed to create GraphServiceClient");

        var tokenResult = Try(() => new Token(logger, keyClient, cfg.KeyVaultOpenIDConnectJwks, credential, graphClient, cfg.WebsiteHostname));
        if (!tokenResult.EnsureSuccess(out var token))
            return tokenResult.FromError("Failed to create Token");

        return token;
    }


    [FunctionAuthorize]
    [Function("Token")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "token")] HttpRequestData req, FunctionContext executionContext, CancellationToken ct)
    {
        var result = await TryRun(req, executionContext, ct);
        if (!result.EnsureSuccess(out var response))
        {
            _logger.LogError(result.Error, "Failed to run Token function");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }

        return response;
    }

    private async Task<Result<HttpResponseData>> TryRun(HttpRequestData req, FunctionContext executionContext, CancellationToken ct)
    {
        var groupResult = TryNotNull(() => req.Query["group_object_id"]);
        if (!groupResult.EnsureSuccess(out var rawGroupString))
            return await groupResult.HttpError(_logger, req, HttpStatusCode.BadRequest, "group_object_id query parameter is required", ct);

        var groupGuidResult = Try(() => Guid.Parse(rawGroupString));
        if (!groupGuidResult.EnsureSuccess(out var groupGuid))
            return await groupGuidResult.HttpError(_logger, req, HttpStatusCode.BadRequest, "group_object_id is not a valid GUID", ct);

        var httpContextResult = TryNotNull(executionContext.GetHttpContext);
        if (!httpContextResult.EnsureSuccess(out var httpContext))
            return await httpContextResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "unable to extract http context from function execution context", ct);

        var requestorObjectIdRawStringResult = TryNotNull(() => httpContext.User.Claims.FirstOrDefault(c => c.Type == "oid")?.Value);
        if (!requestorObjectIdRawStringResult.EnsureSuccess(out var requestorObjectIdRawString))
            return await requestorObjectIdRawStringResult.HttpError(_logger, req, HttpStatusCode.BadRequest, "requestor object id is not a valid GUID", ct);

        var requestorObjectIdResult = Try(() => Guid.Parse(requestorObjectIdRawString));
        if (!requestorObjectIdResult.EnsureSuccess(out var requestorObjectId))
            return await requestorObjectIdResult.HttpError(_logger, req, HttpStatusCode.BadRequest, "requestor object id is not a valid GUID", ct);

        var isMemberResult = await TryIsRequestorMemberOfGroupAsync(groupGuid, requestorObjectId, ct);
        if (!isMemberResult.EnsureSuccess(out var isMember))
            return await isMemberResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "failed to check if requestor is a member of the requested group", ct);

        if (!isMember)
            return await isMemberResult.HttpError(_logger, req, HttpStatusCode.Forbidden, "requestor is not a member of the requested group", ct);

        var tokenResult = await TryCreateTokenAsync(groupGuid, requestorObjectId, ct);
        if (!tokenResult.EnsureSuccess(out var token))
            return await tokenResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "failed to create token", ct);

        var responseResult = Try(() => req.CreateResponse(HttpStatusCode.OK));
        if (!responseResult.EnsureSuccess(out var response))
            return await responseResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "failed to create response", ct);

        var tokenBodyResult = Try(() => new TokenResponse
        {
            AccessToken = token,
            RequestorObjectId = requestorObjectId,
            RequestedGroupObjectId = groupGuid,
        });
        if (!tokenBodyResult.EnsureSuccess(out var tokenBody))
            return await tokenBodyResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "failed to create token response body", ct);

        var writeResult = await Try(() => response.WriteAsJsonAsync(tokenBody, ct));
        if (!writeResult.EnsureSuccess())
            return await writeResult.HttpError(_logger, req, HttpStatusCode.InternalServerError, "failed to write response", ct);

        return response;
    }

    private async Task<Result<string>> TryCreateTokenAsync(Guid subject, Guid requestorObjectId, CancellationToken ct)
    {
        var cryptoProviderFactoryResult = Try(() => new CryptoProviderFactory() { CustomCryptoProvider = new KeyVaultCryptoProvider(_keyClient) });
        if (!cryptoProviderFactoryResult.EnsureSuccess(out var cryptoProviderFactory))
            return cryptoProviderFactoryResult.FromError<CryptoProviderFactory, string>("Failed to create CryptoProviderFactory");

        var signingKeyResult = await Try(() => _keyClient.GetKeyAsync(_keyVaultOpenIDConnectJwks, cancellationToken: ct));
        if (!signingKeyResult.EnsureSuccess(out var signingKey))
            return signingKeyResult.FromError<Azure.Response<KeyVaultKey>, string>("Failed to get signing key");

        var signingRsaKeyResult = Try(() => new KeyVaultRsaSecurityKey(signingKey) { CryptoProviderFactory = cryptoProviderFactory });
        if (!signingRsaKeyResult.EnsureSuccess(out var signingRsaKey))
            return signingRsaKeyResult.FromError<KeyVaultRsaSecurityKey, string>("Failed to create KeyVaultRsaSecurityKey");

        var signingCredentialsResult = Try(() => new SigningCredentials(signingRsaKey, SecurityAlgorithms.RsaSha256));
        if (!signingCredentialsResult.EnsureSuccess(out var signingCredentials))
            return signingCredentialsResult.FromError<SigningCredentials, string>("Failed to create SigningCredentials");

        var nowResult = Try(() => DateTimeOffset.Now);
        if (!nowResult.EnsureSuccess(out var now))
            return nowResult.FromError<DateTimeOffset, string>("Failed to get current time");

        var notBeforeResult = Try(() => now.UtcDateTime);
        if (!notBeforeResult.EnsureSuccess(out var notBefore))
            return notBeforeResult.FromError<DateTime, string>("Failed to get current time");

        var issuedAtResult = Try(() => now.ToUnixTimeSeconds());
        if (!issuedAtResult.EnsureSuccess(out var issuedAt))
            return issuedAtResult.FromError<long, string>("Failed to get current time");

        var expiresResult = Try(() => now.UtcDateTime.AddMinutes(10));
        if (!expiresResult.EnsureSuccess(out var expires))
            return expiresResult.FromError<DateTime, string>("Failed to get current time");

        var claimsResult = Try(() => new List<Claim> {
            new(JwtRegisteredClaimNames.Sub, subject.ToString()),
            new(JwtRegisteredClaimNames.Iat, issuedAt.ToString(), ClaimValueTypes.Integer64),
            new("requestor_oid", requestorObjectId.ToString()),
        });
        if (!claimsResult.EnsureSuccess(out var claims))
            return claimsResult.FromError<List<Claim>, string>("Failed to create claims");

        var tokenResult = Try(() => new JwtSecurityToken(
            issuer: $"https://{_hostname}",
            audience: "api://AzureADTokenExchange",
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: signingCredentials
        ));
        if (!tokenResult.EnsureSuccess(out var token))
            return tokenResult.FromError<JwtSecurityToken, string>("Failed to create JwtSecurityToken");

        var jwtTokenResult = Try(() => new JwtSecurityTokenHandler().WriteToken(token));
        if (!jwtTokenResult.EnsureSuccess(out var jwtToken))
            return jwtTokenResult.FromError<string, string>("Failed to write token");

        return jwtToken;
    }

    private async Task<Result<bool>> TryIsRequestorMemberOfGroupAsync(Guid groupObjectId, Guid requestorObjectId, CancellationToken ct)
    {
        var requestBodyResult = Try(() => new CheckMemberGroupsPostRequestBody { GroupIds = [groupObjectId.ToString()] });
        if (!requestBodyResult.EnsureSuccess(out var requestBody))
            return requestBodyResult.FromError<CheckMemberGroupsPostRequestBody, bool>("Failed to create request body");

        var graphResponseResult = await TryNotNull(() => _graphClient.Users[requestorObjectId.ToString()].CheckMemberGroups.PostAsCheckMemberGroupsPostResponseAsync(requestBody, cancellationToken: ct));
        if (!graphResponseResult.EnsureSuccess(out var graphResponse))
            return graphResponseResult.FromError<CheckMemberGroupsPostResponse, bool>("Failed to check member groups");

        if (graphResponse.Value == null)
        {
            return false;
        }

        return graphResponse.Value.Contains(groupObjectId.ToString());
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public required string AccessToken { get; set; }

        [JsonPropertyName("requestor_object_id")]
        public required Guid RequestorObjectId { get; set; }

        [JsonPropertyName("requested_group_object_id")]
        public required Guid RequestedGroupObjectId { get; set; }
    }
}