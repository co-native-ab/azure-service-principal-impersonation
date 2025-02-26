using Microsoft.Extensions.Configuration;

namespace src;

public class Config(IConfiguration configuration)
{
    public readonly string JwtIssuer = configuration["JWT_ISSUER"] ?? throw new ArgumentNullException("JWT_ISSUER");
    public readonly string JwtAudience = configuration["JWT_AUDIENCE"] ?? throw new ArgumentNullException("JWT_AUDIENCE");
    public readonly string KeyVaultUrl = configuration["KEY_VAULT_URL"] ?? throw new ArgumentNullException("KEY_VAULT_URL");
    public readonly string KeyVaultOpenIDConnectJwks = configuration["KEY_VAULT_OPENID_CONNECT_JWKS"] ?? throw new ArgumentNullException("KEY_VAULT_OPENID_CONNECT_JWKS");
    public readonly string? KeyVaultClientId = configuration["KEY_VAULT_CLIENT_ID"];
    public readonly string WebsiteHostname = configuration["WEBSITE_HOSTNAME"] ?? throw new ArgumentNullException("WEBSITE_HOSTNAME");
}

public static class ConfigurationExtensions
{
    public static Config CreateConfig(this IConfigurationRoot configurationRoot)
    {
        return new Config(configurationRoot);
    }
}