// Original code from: https://gist.github.com/joonaszure/507be31b6213b7daf4ca05c99a201f15
// Credits to Joonas Westlin: https://zure.com/blog/azure-key-vault-sign-and-encrypt-json-web-tokens/

using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.Collections;
using System.Security.Cryptography;

namespace src;

public class KeyVaultCryptoProvider : ICryptoProvider
{
    private readonly KeyClient _keyClient;

    public KeyVaultCryptoProvider(KeyClient keyClient)
    {
        _keyClient = keyClient;
    }

    public bool IsSupportedAlgorithm(string algorithm, params object[] args)
    {

        if (algorithm == SecurityAlgorithms.RsaSha256)
        {
            return true;
        }

        return false;
    }

    public object Create(string algorithm, params object[] args)
    {
        if (args.Length > 0 && args[0] is KeyVaultRsaSecurityKey rsaKey)
        {
            if (algorithm == SecurityAlgorithms.RsaSha256)
            {
                return new KeyVaultKeySignatureProvider(GetCryptographyClient(rsaKey), rsaKey, algorithm);
            }
        }

        throw new ArgumentException($"Unsupported algorithm: {algorithm}, or invalid arguments given", nameof(algorithm));
    }

    public void Release(object cryptoInstance)
    {
    }

    private CryptographyClient GetCryptographyClient(KeyVaultRsaSecurityKey key)
    {
        return _keyClient.GetCryptographyClient(key.KeyName, key.KeyVersion);
    }
}

public class KeyVaultKeySignatureProvider : SignatureProvider
{
    private readonly CryptographyClient _cryptographyClient;

    public KeyVaultKeySignatureProvider(CryptographyClient cryptographyClient, KeyVaultRsaSecurityKey key, string algorithm)
        : base(key, algorithm)
    {
        _cryptographyClient = cryptographyClient;
    }

    public override byte[] Sign(byte[] input)
    {
        if (input == null || input.Length == 0)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var result = _cryptographyClient.SignData(GetKeyVaultAlgorithm(base.Algorithm), input);
        return result.Signature;
    }

    public override bool Verify(byte[] input, byte[] signature)
    {
        if (input == null || input.Length == 0)
        {
            throw new ArgumentNullException(nameof(input));
        }
        if (signature == null || signature.Length == 0)
        {
            throw new ArgumentNullException(nameof(signature));
        }

        var key = (KeyVaultRsaSecurityKey)base.Key;
        using var rsa = key.Key.Key.ToRSA();

        var isValid = rsa.VerifyData(input, signature, GetHashAlgorithm(base.Algorithm), RSASignaturePadding.Pkcs1);
        return isValid;
    }

    public override bool Verify(byte[] input, int inputOffset, int inputLength, byte[] signature, int signatureOffset, int signatureLength)
    {
        if (input == null || input.Length == 0)
        {
            throw new ArgumentNullException(nameof(input));
        }
        if (signature == null || signature.Length == 0)
        {
            throw new ArgumentNullException(nameof(signature));
        }
        if (inputOffset < 0)
        {
            throw new ArgumentException("inputOffset must be greater than 0", nameof(inputOffset));
        }
        if (inputLength < 1)
        {
            throw new ArgumentException("inputLength must be greater than 1", nameof(inputLength));
        }
        if (inputOffset + inputLength > input.Length)
        {
            throw new ArgumentException("inputOffset + inputLength must be greater than input array length");
        }
        if (signatureOffset < 0)
        {
            throw new ArgumentException("signatureOffset must be greater than 0", nameof(signatureOffset));
        }
        if (signatureLength < 1)
        {
            throw new ArgumentException("signatureLength must be greater than 1", nameof(signatureLength));
        }
        if (signatureOffset + signatureLength > signature.Length)
        {
            throw new ArgumentException("signatureOffset + signatureLength must be greater than signature array length");
        }

        byte[] actualSignature;
        if (signature.Length == signatureLength)
        {
            actualSignature = signature;
        }
        else
        {
            var temp = new byte[signatureLength];
            Array.Copy(signature, signatureOffset, temp, 0, signatureLength);
            actualSignature = temp;
        }

        var key = (KeyVaultRsaSecurityKey)base.Key;
        using var rsa = key.Key.Key.ToRSA();

        var isValid = rsa.VerifyData(input, inputOffset, inputLength, actualSignature, GetHashAlgorithm(base.Algorithm), RSASignaturePadding.Pkcs1);
        return isValid;
    }

    protected override void Dispose(bool disposing)
    {
    }

    private static HashAlgorithmName GetHashAlgorithm(string algorithm)
    {
        return algorithm switch
        {
            SecurityAlgorithms.RsaSha256 => HashAlgorithmName.SHA256,
            _ => throw new NotImplementedException(),
        };
    }

    private static SignatureAlgorithm GetKeyVaultAlgorithm(string algorithm)
    {
        return algorithm switch
        {
            SecurityAlgorithms.RsaSha256 => SignatureAlgorithm.RS256,
            _ => throw new NotImplementedException(),
        };
    }
}

public class KeyVaultRsaSecurityKey : AsymmetricSecurityKey
{
    public KeyVaultRsaSecurityKey(KeyVaultKey key)
    {
        Key = key;
        KeyId = key.Properties.Version;
    }

    public KeyVaultKey Key { get; }

    public override int KeySize => new BitArray(Key.Key.N).Length;

    public override string KeyId { get; set; }

    public string KeyName => Key.Properties.Name;
    public string KeyVersion => Key.Properties.Version;

    // In our case we always have a private key in Key Vault
    // (could check supported key operations)
    [Obsolete]
    public override bool HasPrivateKey => true;
    public override PrivateKeyStatus PrivateKeyStatus => PrivateKeyStatus.Exists;
}