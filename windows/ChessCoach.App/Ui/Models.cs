using ChessCoach.Core;

namespace ChessCoach.App.Ui;

public enum CardKind { Suggestion, Review, Hint, Answer, Info }

public sealed record CoachCard(string Id, CardKind Kind, string Title)
{
    public MoveRating? Rating { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
    public string Text { get; init; } = "";
    public bool IsLoading { get; init; }
    public string? Note { get; init; }
    public bool AllowFeedback { get; init; }
    public string? StyleKey { get; init; }
    public int? ReviewPly { get; init; }
    public string? StaleTitle { get; init; }

    public bool Equals(CoachCard? other) =>
        other is not null && Id == other.Id && Kind == other.Kind && Title == other.Title && Rating == other.Rating &&
        Text == other.Text && IsLoading == other.IsLoading && Note == other.Note && AllowFeedback == other.AllowFeedback &&
        StyleKey == other.StyleKey && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode() => HashCode.Combine(Id, Kind, Title, Text, IsLoading, Note);
}

public sealed record PanelAction(string Id, string Title, Action Handler);

public sealed record OverlayState
{
    public readonly record struct Sq(int Col, int Row);
    public readonly record struct Arrow(Sq From, Sq To, bool Partner = false);
    public Arrow? Move { get; init; }
    public Arrow? Threat { get; init; }
    public Sq? Highlight { get; init; }
    public bool PartnerHighlight { get; init; }
    public string? Label { get; init; }
    public string? Hint { get; init; }
}
