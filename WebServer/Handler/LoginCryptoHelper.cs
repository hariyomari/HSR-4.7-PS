using March7thHoney.Internationalization;
using March7thHoney.Util;
using System.Security.Cryptography;
using System.Text;

namespace March7thHoney.WebServer.Handler;

public static class LoginCryptoHelper
{
    private static readonly Logger Logger = new("LoginCrypto");
    private static string SecurityDir => Path.Combine(ConfigManager.Config.Path.ConfigPath, "Security");
    private static string PrivateKeyPath => Path.Combine(SecurityDir, "LoginRsaPrivateKey.pem");

    public static (string account, string password, bool decryptFailed) TryDecryptCredentialsStrict(
        string? account, string? password, bool decryptRequested, bool requireEncryptedPassword = false)
    {
        var rawAccount = account ?? string.Empty;
        var rawPassword = password ?? string.Empty;
        if (!decryptRequested)
            return requireEncryptedPassword ? (string.Empty, string.Empty, true) : (rawAccount, rawPassword, false);

        // Passport clients also send plaintext credentials. A short Base64-looking username or
        // password is not an RSA block; only infer encryption for RSA-sized Base64 values.
        var decryptAccount = LooksLikeRsaCiphertext(rawAccount);
        var decryptPassword = requireEncryptedPassword || LooksLikeRsaCiphertext(rawPassword);
        if (!decryptAccount && !decryptPassword)
            return (rawAccount, rawPassword, false);

        var privatePem = LoadPrivateKey();
        if (string.IsNullOrWhiteSpace(privatePem))
        {
            Logger.Warn(I18NManager.Translate("Server.Log.RsaPrivateKeyMissing", PrivateKeyPath));
            return (string.Empty, string.Empty, true);
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(NormalizePem(privatePem));
            var accountResult = decryptAccount ? TryDecrypt(rawAccount, rsa) : new DecryptResult(rawAccount, true);
            var passwordResult = decryptPassword ? TryDecrypt(rawPassword, rsa) : new DecryptResult(rawPassword, true);
            if (!accountResult.Succeeded || !passwordResult.Succeeded)
                return (string.Empty, string.Empty, true);

            return (accountResult.Value, passwordResult.Value, false);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            Logger.Warn(I18NManager.Translate("Server.Log.RsaPrivateKeyInvalid"));
            return (string.Empty, string.Empty, true);
        }
    }

    private static DecryptResult TryDecrypt(string raw, RSA rsa)
    {
        byte[] cipher;
        try
        {
            cipher = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            return new DecryptResult(string.Empty, false);
        }
        if (cipher.Length != rsa.KeySize / 8)
            return new DecryptResult(string.Empty, false);

        var paddings = new[]
        {
            RSAEncryptionPadding.Pkcs1,
            RSAEncryptionPadding.OaepSHA1,
            RSAEncryptionPadding.OaepSHA256
        };

        foreach (var padding in paddings)
        {
            try
            {
                return new DecryptResult(new UTF8Encoding(false, true).GetString(rsa.Decrypt(cipher, padding)), true);
            }
            catch (Exception ex) when (ex is CryptographicException or DecoderFallbackException)
            {
                // Try the other supported SDK paddings; never reinterpret failed ciphertext as plaintext.
            }
        }

        Logger.Debug($"RSA login decrypt failed for all supported paddings; rejecting login. len={raw.Length}");
        return new DecryptResult(string.Empty, false);
    }

    private static bool LooksLikeRsaCiphertext(string value)
    {
        // Include the 1024-bit ciphertext seen in legacy clients even when the server key is 2048-bit,
        // so a key-size mismatch is rejected instead of auto-registering the ciphertext as an account.
        if (value.Length < 172) return false;
        return Convert.TryFromBase64String(value, new byte[value.Length], out var bytesWritten)
               && bytesWritten >= 128;
    }

    private static string NormalizePem(string pem)
    {
        var normalized = pem.Replace("\\n", "\n").Trim();

        // Accept raw base64 key material in config and wrap it as PEM.
        if (!normalized.Contains("BEGIN", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "-----BEGIN RSA PRIVATE KEY-----\n" + normalized + "\n-----END RSA PRIVATE KEY-----";
        }

        return normalized;
    }

    private static string LoadPrivateKey()
    {
        try
        {
            if (File.Exists(PrivateKeyPath))
                return File.ReadAllText(PrivateKeyPath).Trim();
        }
        catch (Exception e)
        {
            Logger.Debug($"Load private key from file failed: {PrivateKeyPath}", e);
        }

        return ConfigManager.Config.WebSecurity.RsaPrivateKeyPem;
    }

    private readonly record struct DecryptResult(string Value, bool Succeeded);
}
