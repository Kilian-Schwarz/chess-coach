using System.Diagnostics;
using System.Globalization;

namespace ChessCoach.Core;

public sealed record EngineLine(string Move, int? ScoreCP, int? MateIn, IReadOnlyList<string> Pv)
{
    public int Value => MateIn is int m ? (m > 0 ? 10_000 - m * 10 : -10_000 - m * 10) : ScoreCP ?? 0;

    public static string Text(int? cp, int? mate, CultureInfo? culture = null)
    {
        if (mate is int m)
        {
            if (m == 0) return CoreLocale.Get("score.checkmate", culture);
            return CoreLocale.Format(m > 0 ? "score.mateIn" : "score.matedIn", culture, Math.Abs(m));
        }
        if (cp is int c) return (c >= 0 ? "+" : "") + (c / 100.0).ToString("0.0", culture ?? CoreLocale.Culture);
        return "";
    }

    public string ScoreText => Text(ScoreCP, MateIn);
    public string ScoreTextEnglish => Text(ScoreCP, MateIn, CoreLocale.English);
}

public sealed record EngineResult(string Fen, string BestMove, IReadOnlyList<EngineLine> Lines)
{
    public int? ScoreCP => Lines.Count > 0 ? Lines[0].ScoreCP : null;
    public int? MateIn => Lines.Count > 0 ? Lines[0].MateIn : null;
    public int Value => Lines.Count > 0 ? Lines[0].Value : 0;
    public string ScoreText => Lines.Count > 0 ? Lines[0].ScoreText : "";
}

public sealed class StockfishEngine : IDisposable
{
    public int TimeoutSlackMs { get; set; } = 3000;

    public static string? FindBinary(string? configuredPath = null, string? appDirectory = null)
    {
        bool win = OperatingSystem.IsWindows();
        string[] names = win ? new[] { "stockfish.exe" } : new[] { "stockfish" };
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return configuredPath;
        appDirectory ??= AppContext.BaseDirectory;
        foreach (var n in names)
        {
            var p = Path.Combine(appDirectory, "stockfish", n);
            if (File.Exists(p)) return p;

            var dir = Path.Combine(appDirectory, "stockfish");
            if (Directory.Exists(dir))
            {
                var any = Directory.GetFiles(dir, win ? "stockfish*.exe" : "stockfish*").FirstOrDefault();
                if (any != null) return any;
            }
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var n in names)
            {
                try { var p = Path.Combine(dir.Trim('"'), n); if (File.Exists(p)) return p; } catch { }
            }
        if (!win)
            foreach (var p in new[] { "/opt/homebrew/bin/stockfish", "/usr/local/bin/stockfish", "/usr/bin/stockfish", "/usr/games/stockfish" })
                if (File.Exists(p)) return p;
        return null;
    }

    private readonly Process _process = new();
    private readonly object _gate = new();
    private readonly Dictionary<int, EngineLine> _lines = new();
    private List<EngineLine> _previous = new();
    private string _currentFen = "";
    private TaskCompletionSource<EngineResult?>? _completion;
    private int _ignoredBestMoves;
    private int _searchId;
    private bool _stopped;

    public string? TimedOutFen { get; private set; }

    public bool IsRunning
    {
        get { try { return !_stopped && !_process.HasExited; } catch { return false; } }
    }

    public StockfishEngine(string path, int threads = 2, int hashMb = 48)
    {
        _process.StartInfo = new ProcessStartInfo(path)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            string status = "?";
            try { status = _process.ExitCode.ToString(); } catch { }
            string fen; lock (_gate) fen = _currentFen;
            Log.Write($"Stockfish exited with status {status}, last position: {(fen.Length == 0 ? "-" : fen)}");
            lock (_gate) Finish(null);
        };
        _process.Start();
        _process.StandardInput.AutoFlush = true;
        _ = Task.Run(ReadLoop);
        _ = Task.Run(() => { try { _process.StandardError.ReadToEnd(); } catch { } });
        Log.Write($"Stockfish started (PID {_process.Id})");
        threads = Math.Max(1, Math.Min(2, threads));
        hashMb = Math.Max(16, Math.Min(64, hashMb));
        lock (_gate)
        {
            Send("uci");
            Send($"setoption name Threads value {threads}");
            Send($"setoption name Hash value {hashMb}");
            Send("setoption name MultiPV value 3");
            Send("isready");
        }
    }

    public void Dispose() => Stop();

    private void KillProcess()
    {
        try { if (!_process.HasExited) _process.Kill(true); } catch { }
    }

    public void Stop()
    {
        bool run;
        lock (_gate) { run = !_stopped; _stopped = true; Finish(null); }
        if (!run) return;
        try { if (!_process.HasExited) { SendRaw("quit"); if (!_process.WaitForExit(500)) KillProcess(); } } catch { KillProcess(); }
    }

    private void Send(string cmd)
    {
        if (_stopped) return;
        SendRaw(cmd);
    }

    private void SendRaw(string cmd)
    {
        try
        {
            if (_process.HasExited) return;
            _process.StandardInput.WriteLine(cmd);
            _process.StandardInput.Flush();
        }
        catch (Exception e) { Log.Write($"Sending to Stockfish failed: {e.Message}"); }
    }

    public Task<EngineResult?> AnalyzeAsync(string fen, int movetimeMs)
    {
        TaskCompletionSource<EngineResult?> tcs;
        int id;
        lock (_gate)
        {
            if (_completion != null)
            {
                Send("stop");
                _ignoredBestMoves++;
                Finish(null);
            }

            if (!IsRunning)
            {
                Log.Write("Analysis requested but Stockfish is not running");
                return Task.FromResult<EngineResult?>(null);
            }
            _currentFen = fen;
            _lines.Clear();
            _previous = new();
            id = ++_searchId;
            tcs = new TaskCompletionSource<EngineResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = tcs;
            Send($"position fen {fen}");
            Send($"go movetime {movetimeMs}");
        }

        _ = Task.Delay(movetimeMs + TimeoutSlackMs).ContinueWith(_ =>
        {
            bool hung;
            lock (_gate)
            {
                hung = _searchId == id && _completion != null;
                if (hung) { TimedOutFen = fen; Finish(null); }
            }
            if (hung)
            {
                Log.Write($"Stockfish not responding (position {fen}), restarting");
                KillProcess();
            }
        });
        return tcs.Task;
    }

    private void Finish(EngineResult? r)
    {
        var c = _completion;
        _completion = null;
        c?.TrySetResult(r);
    }

    private async Task ReadLoop()
    {
        try
        {
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync()) != null)
                lock (_gate) Handle(line.Trim());
        }
        catch { }
        lock (_gate) Finish(null);
    }

    private void Handle(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        string head = parts[0];
        int i = Array.IndexOf(parts, "score");
        if (head == "info" && _ignoredBestMoves == 0 && i >= 0 && i + 2 < parts.Length)
        {
            if (parts.Contains("lowerbound") || parts.Contains("upperbound")) return;
            int index = 1;
            int mpv = Array.IndexOf(parts, "multipv");
            if (mpv >= 0 && mpv + 1 < parts.Length) index = int.TryParse(parts[mpv + 1], out var x) ? x : 1;
            int? value = int.TryParse(parts[i + 2], out var v) ? v : null;
            int pvi = Array.IndexOf(parts, "pv");
            var pv = pvi >= 0 ? parts[(pvi + 1)..] : Array.Empty<string>();
            if (index == 1 && _lines.Count > 0) { _previous = Merged(); _lines.Clear(); }
            _lines[index] = new EngineLine(pv.Length > 0 ? pv[0] : "",
                parts[i + 1] == "cp" ? value : null, parts[i + 1] == "mate" ? value : null, pv);
        }
        else if (head == "bestmove" && parts.Length >= 2)
        {
            if (_ignoredBestMoves > 0) { _ignoredBestMoves--; return; }
            string move = parts[1] == "(none)" ? "" : parts[1];

            Finish(new EngineResult(_currentFen, move, Merged()));
        }
    }

    private List<EngineLine> Merged()
    {
        var seen = new HashSet<string>();
        return _lines.Keys.OrderBy(k => k).Select(k => _lines[k]).Concat(_previous)
            .Where(l => seen.Add(l.Move)).Take(Math.Max(_lines.Count, _previous.Count)).ToList();
    }
}

public sealed class EngineSession : IDisposable
{
    private readonly Func<string?> _pathProvider;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _crashedFens = new();
    public TimeSpan BlockDuration { get; set; } = TimeSpan.FromSeconds(60);
    private readonly Dictionary<string, (EngineResult Result, int Ms)> _cache = new();
    private StockfishEngine? _engine;
    private Timer? _idleTimer;

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public int Threads { get; set; } = 2;
    public int HashMb { get; set; } = 48;
    public int TimeoutSlackMs { get; set; } = 3000;
    public bool IsEngineRunning { get { lock (_gate) return _engine?.IsRunning == true; } }

    public EngineSession(Func<string?> pathProvider) => _pathProvider = pathProvider;

    private StockfishEngine? Ensure()
    {
        lock (_gate)
        {
            if (_engine is { IsRunning: true }) return _engine;
            _engine?.Dispose(); _engine = null;
            var path = _pathProvider();
            if (path == null) { Log.Write("Stockfish not found"); return null; }
            try { _engine = new StockfishEngine(path, Threads, HashMb) { TimeoutSlackMs = TimeoutSlackMs }; }
            catch (Exception e) { Log.Write($"Stockfish failed to start: {e.Message}"); return null; }
            return _engine;
        }
    }

    private readonly List<(bool Priority, TaskCompletionSource Gate)> _waiters = new();
    private bool _busy;

    private Task Acquire(bool priority)
    {
        lock (_gate)
        {
            if (!_busy) { _busy = true; return Task.CompletedTask; }
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((priority, tcs));
            return tcs.Task;
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_waiters.Count == 0) { _busy = false; return; }
            int i = _waiters.FindIndex(w => w.Priority);
            var next = _waiters[Math.Max(i, 0)];
            _waiters.RemoveAt(Math.Max(i, 0));
            next.Gate.SetResult();
        }
    }

    private EngineResult? Cached(string fen, int movetimeMs, bool priority)
    {
        lock (_gate)
            return _cache.TryGetValue(fen, out var c) && (c.Ms >= movetimeMs || !priority) ? c.Result : null;
    }

    public async Task<EngineResult?> AnalyzeAsync(string fen, int movetimeMs, bool priority = true)
    {
        if (Cached(fen, movetimeMs, priority) is { } hit) return hit;
        if (IsBlocked(fen)) return null;
        await Acquire(priority);
        try
        {
            if (Cached(fen, movetimeMs, priority) is { } again) return again;
            return await RunAsync(fen, movetimeMs);
        }
        finally { Release(); }
    }

    private async Task<EngineResult?> RunAsync(string fen, int movetimeMs)
    {
        var engine = Ensure();
        if (engine == null) return null;
        var result = await engine.AnalyzeAsync(fen, movetimeMs);
        lock (_gate)
        {
            if (result == null && (!engine.IsRunning || engine.TimedOutFen == fen))
            {
                _crashedFens[fen] = DateTime.UtcNow;
                Log.Write($"Engine failed at position {fen}, not sending again");
                if (ReferenceEquals(_engine, engine)) _engine = null;
                engine.Dispose();
            }
            if (result != null)
            {
                if (_cache.Count > 300) _cache.Clear();
                _cache[fen] = (result, movetimeMs);
            }
            ArmIdle();
        }
        return result;
    }

    public bool IsBlocked(string fen)
    {
        lock (_gate)
        {
            if (!_crashedFens.TryGetValue(fen, out var since)) return false;
            if (DateTime.UtcNow - since < BlockDuration) return true;
            _crashedFens.Remove(fen);
            return false;
        }
    }

    private void ArmIdle()
    {
        _idleTimer?.Dispose();
        _idleTimer = _engine == null ? null : new Timer(_ => Shutdown("idle"), null, IdleTimeout, Timeout.InfiniteTimeSpan);
    }

    public void Shutdown(string reason = "")
    {
        StockfishEngine? e;
        lock (_gate)
        {
            _idleTimer?.Dispose(); _idleTimer = null;
            e = _engine; _engine = null;
        }
        if (e != null) { Log.Write($"Stockfish stopping ({reason})"); e.Dispose(); }
    }

    public void Dispose() => Shutdown("Dispose");
}
