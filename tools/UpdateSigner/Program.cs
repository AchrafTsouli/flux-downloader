// Release signing for Apex's in-app updater.
//   keygen <private-key-file> <UpdateSigningKey.cs>   create a P-256 key pair; store the private key, embed the public key
//   sign <private-key-file> <installer.exe> <download-url> <version> <version.json>
// The private key file is protected with DPAPI for the current Windows account. Keep a backup somewhere safe:
// without it, installed copies cannot accept new automatic updates.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftFetch.Core;

if (args.Length >= 3 && args[0] == "keygen")
{
    if (File.Exists(args[1])) { Console.Error.WriteLine("A private key already exists at " + args[1] + ". Refusing to overwrite it."); return 2; }
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    byte[] secret = ProtectedData.Protect(key.ExportPkcs8PrivateKey(), null, DataProtectionScope.CurrentUser);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    File.WriteAllBytes(args[1], secret);
    string publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    string source = File.ReadAllText(args[2]);
    string updated = System.Text.RegularExpressions.Regex.Replace(source, "public const string PublicKey = \"[^\"]*\";", $"public const string PublicKey = \"{publicKey}\";");
    if (updated == source && !source.Contains(publicKey)) { Console.Error.WriteLine("PublicKey constant not found in " + args[2]); return 3; }
    File.WriteAllText(args[2], updated);
    Console.WriteLine("Private key (DPAPI, this Windows account): " + Path.GetFullPath(args[1]));
    Console.WriteLine("Public key embedded in " + args[2] + ": " + publicKey);
    return 0;
}
if (args.Length >= 6 && args[0] == "sign")
{
    using var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(ProtectedData.Unprotect(File.ReadAllBytes(args[1]), null, DataProtectionScope.CurrentUser), out _);
    string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[2]))).ToLowerInvariant();
    string url = args[3], version = args[4];
    if (!Version.TryParse(version, out _) || !Uri.TryCreate(url, UriKind.Absolute, out var link) || link.Scheme != "https") { Console.Error.WriteLine("Use a numeric version and an HTTPS download URL."); return 4; }
    string signature = Convert.ToBase64String(key.SignData(AppUpdates.SignedPayload(version, url, sha), HashAlgorithmName.SHA256));
    var feed = new { version, url, sha256 = sha, signature };
    File.WriteAllText(args[5], JsonSerializer.Serialize(feed, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    // Self-check with the same verifier the app uses.
    AppUpdates.PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    if (!AppUpdates.Parse(File.ReadAllBytes(args[5])).Signed) { Console.Error.WriteLine("Signature self-check failed."); return 5; }
    Console.WriteLine($"Signed {version}: {args[5]} (sha256 {sha})");
    return 0;
}
Console.Error.WriteLine("Usage: keygen <private-key-file> <UpdateSigningKey.cs> | sign <private-key-file> <installer.exe> <url> <version> <version.json>");
return 1;
