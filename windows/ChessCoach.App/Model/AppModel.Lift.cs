using ChessCoach.App.Ui;
using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed partial class AppModel
{
    private Placement? _liftPlacement;
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

    private bool HandleLift(Placement placement, PieceColor me, bool whiteAtBottom)
    {
        if (_liftPlacement != null && placement.Equals(_liftPlacement)) return true;
        if (_record.Current is not { } last || placement.Equals(_stablePlacement) || LiftedPiece.Detect(last, placement) is not { } lifted)
        {
            ClearLift();
            return false;
        }
        _liftPlacement = placement;
        _settled = true;
        if (!Settings.DragHints || !IsCoached(last.Turn, me))
        {
            SetLift(null, null);
            return true;
        }
        bool faded = _hintLevel == 0 && (ProfileFor(last.Turn, me)?.SupportFaded ?? false);
        var unsafeSquares = faded ? new() : lifted.UnsafeSquares(last);
        var marks = unsafeSquares
            .Select(u => Placement.ScreenSquare(u.File, u.Rank, whiteAtBottom))
            .Select(s => new OverlayState.Sq(s.Col, s.Row)).ToArray();
        string? text = LiftedPiece.Summary(unsafeSquares, lifted.Piece, Loc.Culture);
        SetLift(marks.Length == 0 ? null : marks, text == null ? null : new CoachCard($"lift|{lifted.Square}|{last.Fen}", CardKind.Info, text));
        return true;
    }

    private void SetLift(OverlayState.Sq[]? marks, CoachCard? card)
    {
        _liftMarks = marks;
        _liftCard = card;
        if (_overlayBase is { } b && _overlayRect.Width > 0) _overlay.Show(_overlayRect, b with { Unsafe = marks });
        UpdatePanel();
    }

    private void ClearLift()
    {
        if (_liftPlacement == null && _liftMarks == null && _liftCard == null) return;
        _liftPlacement = null;
        SetLift(null, null);
    }
}
