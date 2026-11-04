using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed class GameBook
{
    public Queue<PlayedMove> Queue { get; } = new();
    public List<ReviewedMove> Reviewed { get; } = new();
    public HashSet<string> Rated { get; } = new();
    public Dictionary<string, int> HintUsed { get; } = new();
    public int Plies => Queue.Count + Reviewed.Count;
}

public sealed record RecapProgress(string Id, int Done, int Total);
