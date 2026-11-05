namespace ChessCoach.Core;

public sealed class RelearnGuard
{
    public const int BadFrames = 3;

    private int _frames;
    private BoardRect? _rect;

    public int Count => _frames;

    public void Reset() { _frames = 0; _rect = null; }

    public bool Observe(bool bad, BoardRect rect, int required = BadFrames)
    {
        if (!bad) { Reset(); return false; }
        if (_rect is { } r && r == rect) _frames++;
        else { _rect = rect; _frames = 1; }
        return _frames >= required;
    }
}

public sealed class StuckRecovery
{
    public static readonly TimeSpan After = TimeSpan.FromSeconds(3);

    private DateTime? _since;

    public bool Observe(bool mismatch, DateTime now)
    {
        if (!mismatch) { _since = null; return false; }
        _since ??= now;
        if (now - _since <= After) return false;
        _since = null;
        return true;
    }

    public void Reset() => _since = null;
}
