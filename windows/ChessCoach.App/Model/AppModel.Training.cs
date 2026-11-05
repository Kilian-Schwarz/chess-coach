using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed partial class AppModel
{
    public Task<EngineResult?> AnalyzePuzzleAsync(string fen) => _engine.AnalyzeAsync(fen, 300, false);

    public void RequestPuzzleWhy(Puzzle puzzle, PlayerProfile profile, Action<string> done)
    {
        if (!LlmConfigured || ChessPosition.FromFen(puzzle.Fen) is not { } pos
            || PuzzleCoach.WhyRequest(puzzle, profile.PromptContext, profile.Style.Current) is not { } req) return;
        var allowed = FactCheck.Allowed(new[] { pos }, new[] { (IReadOnlyList<string>)puzzle.BestLine });
        Explain(req.System, req.User, req.MaxTokens, (text, final) =>
        {
            if (!final) return;
            var cleaned = FactCheck.Clean(Coach.CleanAnswer(text), allowed);
            if (cleaned.Removed == 0 && !string.IsNullOrEmpty(cleaned.Text)) done(cleaned.Text);
        }, _ => { });
    }
}
