using System.Net;
using System.Text;
using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

[Collection("LlmHttp")]
public class LlmClientTests : IDisposable
{
    static readonly string? Live = Environment.GetEnvironmentVariable("CHESSCOACH_TEST_LLM_URL");
    static readonly string? LiveModel = Environment.GetEnvironmentVariable("CHESSCOACH_TEST_LLM_MODEL");
    readonly HttpClient _orig = LlmClient.Http;
    public void Dispose() => LlmClient.Http = _orig;

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public List<string> Urls = new(); public List<string> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Urls.Add(r.RequestUri!.ToString());
            Bodies.Add(r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct));
            return f(r);
        }
    }

    static HttpResponseMessage Json(string s, HttpStatusCode c = HttpStatusCode.OK) =>
        new(c) { Content = new StringContent(s, Encoding.UTF8, "application/json") };

    static string Sse(params string[] pieces) =>
        string.Concat(pieces.Select(p => "data: {\"choices\":[{\"delta\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(p) + "}}]}\n\n")) + "data: [DONE]\n\n";

    [Fact]
    public async Task DiscoverTriesBaseThenApiThenV1()
    {
        var h = new Handler(r => r.RequestUri!.AbsolutePath == "/v1/models"
            ? Json("{\"data\":[{\"id\":\"m1\"},{\"id\":\"m2\",\"name\":\"Two\"}]}") : Json("<html>", HttpStatusCode.NotFound));
        LlmClient.Http = new HttpClient(h);
        var (b, models) = await LlmClient.DiscoverAsync("srv.example/", "tok");
        Assert.Equal("https://srv.example/v1", b);
        Assert.Equal(new[] { "m1", "m2" }, models.Select(m => m.Id));
        Assert.Equal("Two", models[1].Name);
        Assert.Equal("m1", models[0].Name);
        Assert.Equal(new[] { "https://srv.example/models", "https://srv.example/api/models", "https://srv.example/v1/models" }, h.Urls);
    }

    [Fact]
    public async Task DiscoverStopsOn401()
    {
        LlmClient.Http = new HttpClient(new Handler(_ => Json("no", HttpStatusCode.Unauthorized)));
        var e = await Assert.ThrowsAsync<LlmException>(() => LlmClient.DiscoverAsync("https://x", ""));
        Assert.Equal("Access denied. Please check the token.", e.Message);
    }

    [Fact]
    public async Task DiscoverNoModels()
    {
        LlmClient.Http = new HttpClient(new Handler(_ => Json("{\"data\":[]}")));
        var e = await Assert.ThrowsAsync<LlmException>(() => LlmClient.DiscoverAsync("https://x", ""));
        Assert.Equal(LlmException.Kind.NoModels, e.Reason);
        await Assert.ThrowsAsync<LlmException>(() => LlmClient.DiscoverAsync("  ", ""));
    }

    [Fact]
    public async Task ExplainStreamsAndStripsThinkingWithTokenHeader()
    {
        string? auth = null;
        var h = new Handler(r =>
        {
            auth = r.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sse("<think>he", "llo</think>Hal", "lo ", "Welt"), Encoding.UTF8, "text/event-stream") };
        });
        LlmClient.Http = new HttpClient(h);
        var seen = new List<string>();
        var full = await LlmClient.ExplainAsync("http://x/v1", "abc", "m", "sys", "usr", seen.Add);
        Assert.Equal("Hallo Welt", full);
        Assert.Equal("Bearer abc", auth);
        Assert.Equal("Hallo Welt", seen[^1]);
        Assert.Contains("\"enable_thinking\":false", h.Bodies[0]);
        Assert.Contains("\"stream\":true", h.Bodies[0]);
        Assert.EndsWith("/chat/completions", h.Urls[0]);
    }

    [Fact]
    public async Task ExplainRetriesWithoutThinkingParameterOn400()
    {
        int n = 0;
        var h = new Handler(r => n++ == 0 ? Json("bad param", HttpStatusCode.BadRequest)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sse("ok"), Encoding.UTF8, "text/event-stream") });
        LlmClient.Http = new HttpClient(h);
        Assert.Equal("ok", await LlmClient.ExplainAsync("http://x/v1", "", "m", "s", "u"));
        Assert.Equal(2, h.Bodies.Count);
        Assert.Contains("chat_template_kwargs", h.Bodies[0]);
        Assert.DoesNotContain("chat_template_kwargs", h.Bodies[1]);
    }

    [Fact]
    public async Task ExplainOtherHttpErrorIsThrown()
    {
        LlmClient.Http = new HttpClient(new Handler(_ => Json("boom", HttpStatusCode.InternalServerError)));
        var e = await Assert.ThrowsAsync<LlmException>(() => LlmClient.ExplainAsync("http://x/v1", "", "m", "s", "u"));
        Assert.Equal("The server reported error 500.", e.Message);
        Assert.DoesNotContain("boom", e.Message);
    }

    [Fact]
    public async Task NotFoundHasItsOwnText()
    {
        LlmClient.Http = new HttpClient(new Handler(_ => Json("{}", HttpStatusCode.NotFound)));
        var e = await Assert.ThrowsAsync<LlmException>(() => LlmClient.ExplainAsync("http://x/v1", "", "m", "s", "u"));
        Assert.Equal("The server did not find the address or model (error 404).", e.Message);
    }

    static async Task<bool> LiveReachable()
    {
        if (string.IsNullOrEmpty(Live) || string.IsNullOrEmpty(LiveModel)) return false;
        try
        {
            using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            return (await c.GetAsync(Live + "/models")).IsSuccessStatusCode;
        }
        catch { return false; }
    }

    [Fact]
    public async Task LiveServer()
    {
        if (!await LiveReachable()) return;
        LlmClient.Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var (b, models) = await LlmClient.DiscoverAsync(Live!, "");
        Assert.Equal(Live, b);
        Assert.Contains(models, m => m.Id == LiveModel);
        var text = await LlmClient.ExplainAsync(b, "", LiveModel!, "Answer in one short sentence.", "Say hello.");
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("<think>", text);
    }
}
