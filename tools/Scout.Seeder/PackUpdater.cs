using System.Security.Cryptography;
using System.Text.Json;
using Scout.Core;

namespace Scout.Seeder;

public sealed record UpdateManifest(int SchemaVersion, string GameVersion, string PackVersion, string Url, string Sha256);

/// <summary>Explicit development/update command; never referenced by the desktop runtime.</summary>
public static class PackUpdater
{
    public static async Task<StrategyPack> Download(HttpClient client, Uri manifestUri, string gameVersion, CancellationToken cancellation = default)
    {
        static void Safe(Uri uri)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) throw new InvalidDataException("Updates require HTTPS (HTTP allowed only for loopback testing)");
        }
        Safe(manifestUri);
        async Task<byte[]> Read(Uri uri, int limit)
        {
            Safe(uri);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } final) Safe(final);
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update exceeds size limit");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation); using var memory = new MemoryStream();
            var buffer = new byte[8192]; int n;
            while ((n = await stream.ReadAsync(buffer, cancellation)) != 0)
            {
                if (memory.Length + n > limit) throw new InvalidDataException("Update exceeds size limit");
                memory.Write(buffer, 0, n);
            }
            return memory.ToArray();
        }
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(await Read(manifestUri, 16_384), Json.Options) ?? throw new InvalidDataException("Empty manifest");
        if (manifest.SchemaVersion != 1 || manifest.GameVersion != gameVersion || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid/incompatible update manifest");
        var bytes = await Read(new Uri(manifest.Url, UriKind.Absolute), 5_000_000);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update hash mismatch");
        var pack = JsonSerializer.Deserialize<StrategyPack>(bytes, Json.Options) ?? throw new InvalidDataException("Empty pack"); PackValidation.Validate(pack);
        if (pack.GameVersion != gameVersion || pack.PackVersion != manifest.PackVersion || pack.Catalog == null) throw new InvalidDataException("Manifest and catalog disagree");
        return pack;
    }
}
