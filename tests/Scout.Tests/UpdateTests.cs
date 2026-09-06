using System.Net;
using System.Security.Cryptography;
using System.Text;
using Scout.Core;
using Scout.Seeder;
using Xunit;

namespace Scout.Tests;

public sealed class UpdateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MockHttpDownloadChecksHashBeforeAnyInstallation(bool tamper)
    {
        var pack = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
        var parsed = Json.Read<StrategyPack>(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
        var manifest = new UpdateManifest(1, parsed.GameVersion, parsed.PackVersion, "http://localhost/pack", tamper ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(pack)));
        using var client = new HttpClient(new MockHttp(Encoding.UTF8.GetBytes(Json.Write(manifest)), pack));
        if (tamper) await Assert.ThrowsAsync<InvalidDataException>(() => PackUpdater.Download(client, new Uri("http://localhost/manifest"), parsed.GameVersion));
        else Assert.Equal(parsed.PackVersion, (await PackUpdater.Download(client, new Uri("http://localhost/manifest"), parsed.GameVersion)).PackVersion);
    }
    [Fact]
    public async Task UnencryptedRemoteUpdateIsRejected()
    {
        using var client = new HttpClient(new MockHttp([], []));
        await Assert.ThrowsAsync<InvalidDataException>(() => PackUpdater.Download(client, new Uri("http://example.com/manifest"), "0.107.1"));
    }
    private sealed class MockHttp(byte[] manifest, byte[] pack) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath == "/manifest" ? manifest : pack), RequestMessage = request });
    }
}
