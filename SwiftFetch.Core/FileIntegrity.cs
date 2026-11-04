using System.Security.Cryptography;

namespace SwiftFetch.Core;

public static class FileIntegrity
{
    public static async Task<string> ComputeHashAsync(string filePath, string algorithm, CancellationToken token = default)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("File not found", filePath);

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        using HashAlgorithm hasher = algorithm.ToUpperInvariant() switch
        {
            "MD5" => MD5.Create(),
            "SHA1" or "SHA-1" => SHA1.Create(),
            "SHA256" or "SHA-256" => SHA256.Create(),
            "SHA512" or "SHA-512" => SHA512.Create(),
            _ => throw new ArgumentException("Unsupported hash algorithm: " + algorithm, nameof(algorithm))
        };

        byte[] hash = await hasher.ComputeHashAsync(stream, token);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<bool> VerifyAsync(string filePath, string expectedHash, CancellationToken token = default)
    {
        string trimmed = expectedHash.Trim().ToLowerInvariant();
        string algorithm = trimmed.Length switch
        {
            32 => "MD5",
            40 => "SHA-1",
            64 => "SHA-256",
            128 => "SHA-512",
            _ => throw new ArgumentException("Unrecognized hash format/length.")
        };

        string actual = await ComputeHashAsync(filePath, algorithm, token);
        return string.Equals(actual, trimmed, StringComparison.OrdinalIgnoreCase);
    }
}
