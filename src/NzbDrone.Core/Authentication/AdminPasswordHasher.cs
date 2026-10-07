using System;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Core.Authentication;

public static class AdminPasswordHasher
{
    private const string FormatPrefix = "v1";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string HashPassword(string plainTextPassword)
    {
        ArgumentNullException.ThrowIfNull(plainTextPassword);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Pbkdf2(plainTextPassword, salt);
        return string.Join(
            '$',
            FormatPrefix,
            Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool VerifyPassword(string plainTextPassword, string storedValue)
    {
        if (string.IsNullOrEmpty(plainTextPassword) || string.IsNullOrEmpty(storedValue))
        {
            return false;
        }

        if (!storedValue.StartsWith(FormatPrefix + "$", StringComparison.Ordinal))
        {
            return FixedTimeEquals(plainTextPassword, storedValue);
        }

        var parts = storedValue.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            var actualHash = Pbkdf2(plainTextPassword, salt, iterations, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        Span<byte> hashA = stackalloc byte[32];
        Span<byte> hashB = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(a), hashA);
        SHA256.HashData(Encoding.UTF8.GetBytes(b), hashB);
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }

    private static byte[] Pbkdf2(string password, byte[] salt, int iterations = Iterations, int outputBytes = HashSize)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            outputBytes);
    }
}
