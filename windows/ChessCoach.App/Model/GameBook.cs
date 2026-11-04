using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed class GameBook
{
    public Queue<PlayedMove> Queue { get; } = new();
    public List<ReviewedMove> Reviewed { get; } = new();
    public HashSet<string> Rated { get; } = new();
    public Dictionary<string, int> HintUsed { get; } = new();
    public Dictionary<PieceColor, List<GameMistake>> Mistakes { get; } = new();
    public HashSet<string> Seen { get; } = new();
    public int Plies => Queue.Count + Reviewed.Count;

    public static string Key(string fenBefore, string uci) => $"{fenBefore}|{uci}";

    public bool Add(PlayedMove pm)
    {
        if (!Seen.Add(Key(pm.FenBefore, pm.Uci))) return false;
        Queue.Enqueue(pm);
        return true;
    }

    public void Retain(IReadOnlySet<string> valid)
    {
        var kept = Queue.Where(m => valid.Contains(Key(m.FenBefore, m.Uci))).ToList();
        Queue.Clear();
        foreach (var m in kept) Queue.Enqueue(m);
        Reviewed.RemoveAll(r => !valid.Contains(Key(r.FenBefore, r.Uci)));
        Rated.RemoveWhere(k => !valid.Contains(k));
        Seen.RemoveWhere(k => !valid.Contains(k));
    }
}

public sealed record RecapProgress(string Id, int Done, int Total);
