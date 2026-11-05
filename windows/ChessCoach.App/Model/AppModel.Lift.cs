using ChessCoach.App.Ui;
using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed partial class AppModel
{
    private string? _liftKey;
    private OverlayState.Sq[]? _liftMarks;
    private CoachCard? _liftCard;
    private OverlayState? _overlayBase;
    private System.Drawing.Rectangle _overlayRect;

    private void ShowOverlay(System.Drawing.Rectangle rect, OverlayState state)
    {
        _overlayBase = state;
        _overlayRect = rect;
        _overlay.Show(rect, state with { Unsafe = _liftMarks });
    }

    private void HandleLift(Placement placement, PieceColor me, bool whiteAtBottom, bool held)
    {
        if (!Settings.DragHints || _record.Current is not { } last || last.Board.Equals(placement) || !IsCoached(last.Turn, me)
            || LiftedPiece.Detect(last, placement, held) is not { } lifted)
        {
            ClearLift();
            return;
        }
        bool faded = _hintLevel == 0 && (ProfileFor(last.Turn, me)?.SupportFaded ?? false);
        string key = $"{last.Fen}|{lifted.Square}|{faded}";
        if (_liftKey == key) return;
        _liftKey = key;
        var unsafeSquares = faded ? new() : lifted.UnsafeSquares(last);
        var marks = unsafeSquares
            .Select(u => Placement.ScreenSquare(u.File, u.Rank, whiteAtBottom))
            .Select(s => new OverlayState.Sq(s.Col, s.Row)).ToArray();
        string? text = LiftedPiece.Summary(unsafeSquares, lifted.Piece, Loc.Culture);
        SetLift(marks.Length == 0 ? null : marks, text == null ? null : new CoachCard($"lift|{key}", CardKind.Info, text));
    }

    private void SetLift(OverlayState.Sq[]? marks, CoachCard? card)
    {
        _liftMarks = marks;
        _liftCard = card;
        if (_boardPresent && _overlayBase is { } b && _overlayRect.Width > 0) _overlay.Show(_overlayRect, b with { Unsafe = marks });
        UpdatePanel();
    }

    private void ClearLift()
    {
        if (_liftKey == null && _liftMarks == null && _liftCard == null) return;
        _liftKey = null;
        SetLift(null, null);
    }
}
