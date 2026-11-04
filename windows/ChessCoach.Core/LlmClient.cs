using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChessCoach.Core;

public sealed record LlmModel(string Id, string Name);

public sealed class LlmException : Exception
{
    public enum Kind { BadUrl, Http, NoModels, NotConfigured }
    public Kind Reason { get; }
    public int StatusCode { get; }
    public string Body { get; }

    public LlmException(Kind kind, int status = 0, string body = "") : base(Describe(kind, status, body))
    { Reason = kind; StatusCode = status; Body = body; }

    private static string Describe(Kind kind, int code, string body) => kind switch
    {
        Kind.BadUrl => CoreLocale.Get("llm.badUrl"),
        Kind.Http when code is 401 or 403 => CoreLocale.Get("llm.denied"),
        Kind.Http => CoreLocale.Format("llm.httpError", null, code, body.Length > 160 ? body[..160] : body),
        Kind.NoModels => CoreLocale.Get("llm.noModels"),
        _ => CoreLocale.Get("llm.notConfigured"),
    };
}

public interface ITokenStore
{
    string Load();
    void Save(string token);
}

public static class LlmClient
{
    public static HttpClient Http { get; set; } = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<(string Base, List<LlmModel> Models)> DiscoverAsync(string input, string token, CancellationToken ct = default)
    {
        var trimmed = input.Trim();
        while (trimmed.EndsWith('/')) trimmed = trimmed[..^1];
        if (trimmed.Length == 0) throw new LlmException(LlmException.Kind.BadUrl);
        if (!trimmed.Contains("://")) trimmed = "https://" + trimmed;
        Exception last = new LlmException(LlmException.Kind.NoModels);
        foreach (var b in new[] { trimmed, trimmed + "/api", trimmed + "/v1" })
        {
            try
            {
                var models = await ListModelsAsync(b, token, ct);
                if (models.Count > 0) return (b, models);
            }
            catch (LlmException e)
            {
                if (e.Reason == LlmException.Kind.Http && e.StatusCode is 401 or 403) throw;
                last = e;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { last = e; }
        }
        throw last;
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string token)
    {
        Uri? uri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) throw new LlmException(LlmException.Kind.BadUrl);
        var req = new HttpRequestMessage(method, uri);
        if (token.Length > 0) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        return req;
    }

    public static async Task<List<LlmModel>> ListModelsAsync(string @base, string token, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var req = Request(HttpMethod.Get, @base + "/models", token);
        using var resp = await Http.SendAsync(req, cts.Token);
        string text = await resp.Content.ReadAsStringAsync(cts.Token);
        if (resp.StatusCode != HttpStatusCode.OK) throw new LlmException(LlmException.Kind.Http, (int)resp.StatusCode, text);
        try
        {
            var data = JsonNode.Parse(text)?["data"]?.AsArray() ?? throw new LlmException(LlmException.Kind.NoModels);
            var list = new List<LlmModel>();
            foreach (var m in data)
            {
                var id = m?["id"]?.GetValue<string>();
                if (id == null) continue;
                list.Add(new LlmModel(id, m?["name"] is JsonValue v && v.TryGetValue<string>(out var n) ? n : id));
            }
            return list;
        }
        catch (JsonException) { throw new LlmException(LlmException.Kind.NoModels); }
        catch (InvalidOperationException) { throw new LlmException(LlmException.Kind.NoModels); }
    }

    public static async Task<string> ExplainAsync(string @base, string token, string model, string system, string user,
        Action<string>? onText = null, CancellationToken ct = default, int? maxTokens = null)
    {
        try { return await StreamAsync(@base, token, model, system, user, true, onText, ct, maxTokens); }
        catch (LlmException e) when (e.Reason == LlmException.Kind.Http && e.StatusCode == 400)
        {
            return await StreamAsync(@base, token, model, system, user, false, onText, ct, maxTokens);
        }
    }

    public static string BuildBody(string model, string system, string user, bool disableThinking, int? maxTokens = null)
    {
        var body = new JsonObject
        {
            ["model"] = model, ["stream"] = true, ["temperature"] = 0.4, ["max_tokens"] = maxTokens ?? 700,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        if (disableThinking) body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        return body.ToJsonString();
    }

    private static async Task<string> StreamAsync(string @base, string token, string model, string system, string user,
        bool disableThinking, Action<string>? onText, CancellationToken ct, int? maxTokens = null)
    {
        using var req = Request(HttpMethod.Post, @base + "/chat/completions", token);
        req.Content = new StringContent(BuildBody(model, system, user, disableThinking, maxTokens), Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            string text = await resp.Content.ReadAsStringAsync(cts.Token);
            throw new LlmException(LlmException.Kind.Http, (int)resp.StatusCode, text.Length > 400 ? text[..400] : text);
        }
        using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var full = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) != null)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(120));
            ct.ThrowIfCancellationRequested();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string payload = line[5..].Trim();
            if (payload == "[DONE]") break;
            string? piece;
            try
            {
                var choice = JsonNode.Parse(payload)?["choices"]?[0];
                var delta = choice?["delta"] ?? choice?["message"];
                piece = delta?["content"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            }
            catch (JsonException) { continue; }
            if (string.IsNullOrEmpty(piece)) continue;
            full.Append(piece);
            onText?.Invoke(Coach.CleanAnswer(full.ToString()));
        }
        return Coach.CleanAnswer(full.ToString());
    }
}
