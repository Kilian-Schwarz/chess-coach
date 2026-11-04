using System.Security.Cryptography;
using ChessCoach.App.Platform;

namespace ChessCoach.App.Tests;

public class UpdaterTests
{
    private static (byte[] Data, string Sha, string Signature, string PublicKey) Signed(byte[] data)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return (data, Convert.ToHexString(SHA256.HashData(data)), Convert.ToBase64String(signature),
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    [Theory]
    [InlineData("0.2.0", "0.1.9", true)]
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("v1.0", "1.0.0", false)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("1.0.0-beta", "0.9.9", true)]
    public void VersionCompare(string candidate, string current, bool expected) =>
        Assert.Equal(expected, Updater.IsNewer(candidate, current));

    [Theory]
    [InlineData("0.2.0", "0.2.0", "0.1.0", true)]
    [InlineData("0.2.0", "0.3.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.0", "0.1.0", false)]
    [InlineData("0.0.9", "0.0.9", "0.1.0", false)]
    [InlineData("0.2.0-beta", "0.2.0-beta", "0.1.0", false)]
    [InlineData("", "", "0.1.0", false)]
    public void StagedVersionMustMatchFeedAndBeNewer(string staged, string feed, string current, bool expected) =>
        Assert.Equal(expected, Updater.IsAcceptableUpdate(staged, feed, current));

    [Fact]
    public void ReadsFileVersionOfAnAssembly() =>
        Assert.Matches(@"^\d+\.\d+\.\d+$", Updater.ReadVersion(typeof(Updater).Assembly.Location));

    [Theory]
    [InlineData("""{"version":"0.2.0","windows":{"file":"../x.zip"}}""")]
    [InlineData("""{"version":"0.2.0 x","windows":{"file":"ChessCoach-0.2.0-windows-x64.zip"}}""")]
    [InlineData("""{"version":"0.2","windows":{"file":"ChessCoach-0.2-windows-x64.zip"}}""")]
    public void RejectsUnsafeFeedValues(string json) => Assert.Null(Updater.Parse(json));

    [Fact]
    public void ParsesManifest()
    {
        var m = Updater.Parse("""{"version":"0.2.0","notes":"n","macos":{"file":"a.zip"},"windows":{"file":"ChessCoach-0.2.0-windows-x64.zip","sha256":"ab","signature":"cd"}}""");
        Assert.Equal(new UpdateManifest("0.2.0", "n", "ChessCoach-0.2.0-windows-x64.zip", "ab", "cd"), m);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":"1.0.0"}""")]
    [InlineData("""{"version":"","windows":{"file":"x"}}""")]
    public void RejectsBrokenManifest(string json) => Assert.Null(Updater.Parse(json));

    [Fact]
    public void AcceptsValidSignature()
    {
        var s = Signed([1, 2, 3, 4]);
        Assert.True(Updater.Verify(s.Data, s.Sha, s.Signature, s.PublicKey));
    }

    [Fact]
    public void RejectsTamperedData()
    {
        var s = Signed([1, 2, 3, 4]);
        Assert.False(Updater.Verify([1, 2, 3, 5], s.Sha, s.Signature, s.PublicKey));
    }

    [Fact]
    public void RejectsWrongHashOrKey()
    {
        var s = Signed([1, 2, 3, 4]);
        var other = Signed([9]);
        Assert.False(Updater.Verify(s.Data, new string('0', 64), s.Signature, s.PublicKey));
        Assert.False(Updater.Verify(s.Data, s.Sha, s.Signature, other.PublicKey));
        Assert.False(Updater.Verify(s.Data, s.Sha, "%%%", s.PublicKey));
    }

    [Fact]
    public void BuiltInKeyIsValid()
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(Updater.PublicKey), out _);
    }

    private sealed class Stub(System.Net.HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task MissingFeedMeansNoUpdate()
    {
        using var http = new HttpClient(new Stub(System.Net.HttpStatusCode.NotFound, "Not Found"));
        Assert.Null(await Updater.FetchAsync(http));
    }

    [Fact]
    public async Task ServerErrorStillThrows()
    {
        using var http = new HttpClient(new Stub(System.Net.HttpStatusCode.InternalServerError, ""));
        await Assert.ThrowsAsync<HttpRequestException>(() => Updater.FetchAsync(http));
    }
}
