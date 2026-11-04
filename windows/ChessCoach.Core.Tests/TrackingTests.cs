using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class TrackingTests
{
    const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    const string S1 = "rnbqkbnr/pppppppp/8/8/8/5N2/PPPPPPPP/RNBQKB1R b KQkq - 1 1";
    const string S2 = "rnbqkbnr/ppp1pppp/8/3p4/8/5N2/PPPPPPPP/RNBQKB1R w KQkq - 0 2";
    const string S3 = "rnbqkbnr/ppp1pppp/8/3p4/8/5NP1/PPPPPP1P/RNBQKB1R b KQkq - 0 2";
    const string S4 = "rnbqkbnr/pp2pppp/8/2pp4/8/5NP1/PPPPPP1P/RNBQKB1R w KQkq - 0 3";
    const string Lifted = "rnbqkbnr/pp2pppp/8/2pp4/8/5NP1/PPPPPP1P/RNBQK2R w KQkq - 0 3";

    static Placement P(string fen) => ChessPosition.FromFen(fen)!.Board;

    static GameRecord Begin()
    {
        var rec = new GameRecord();
        Assert.Equal(GameChange.NewGame, rec.Update(P(Start), true).Change);
        return rec;
    }

    [Fact]
    public void SingleMovesAreReviewable()
    {
        var rec = Begin();
        foreach (var (fen, uci, mover) in new[] { (S1, "g1f3", PieceColor.White), (S2, "d7d5", PieceColor.Black), (S3, "g2g3", PieceColor.White), (S4, "c7c5", PieceColor.Black) })
        {
            var u = rec.Update(P(fen), true);
            Assert.Equal(GameChange.Moves, u.Change);
            Assert.Single(u.Reviewable);
            Assert.Equal(uci, u.Reviewable[0].Uci);
            Assert.Equal(mover, u.Reviewable[0].Mover);
            Assert.False(u.Gap);
            Assert.StartsWith("Move ", Assert.Single(u.Log));
            var again = rec.Update(P(fen), true);
            Assert.Equal(GameChange.Unchanged, again.Change);
            Assert.Empty(again.Log);
        }
        Assert.Equal(4, rec.Moves.Count);
    }

    [Fact]
    public void FastBotReplyGivesTwoReviewablePlies()
    {
        var rec = Begin();
        rec.Update(P(S1), true);
        var u = rec.Update(P(S3), true);
        Assert.Equal(new[] { "d7d5", "g2g3" }, u.NewMoves.Select(m => m.Uci));
        Assert.Equal(new[] { "d7d5", "g2g3" }, u.Reviewable.Select(m => m.Uci));
        Assert.StartsWith("Moves (2 plies", u.Log[0]);
    }

    [Fact]
    public void ThreePliesMissedIsAGapWithoutReview()
    {
        var rec = Begin();
        rec.Update(P(S1), true);
        var u = rec.Update(P(S4), true);
        Assert.Equal(GameChange.Resync, u.Change);
        Assert.True(u.Gap);
        Assert.Empty(u.Reviewable);
        Assert.Single(rec.Moves);
        Assert.Equal(new[] { 1 }, rec.Breaks);
        Assert.Equal(0, rec.SegmentMoves);
        Assert.True(u.Estimated);
        Assert.StartsWith("Resync:", u.Log[0]);
    }

    [Fact]
    public void WrongSideGuessIsCorrectedWithoutReview()
    {
        var rec = new GameRecord();
        rec.Update(P(S1), true);
        var u = rec.Update(P(S2), true);
        Assert.Equal(GameChange.Moves, u.Change);
        Assert.Empty(u.Reviewable);
        Assert.StartsWith("Side override", u.Log[0]);
    }

    [Fact]
    public void LiftedPieceIsIgnoredAndPutBackIsUnchanged()
    {
        var rec = Begin();
        foreach (var f in new[] { S1, S2, S3, S4 }) rec.Update(P(f), true);
        var u = rec.Update(P(Lifted), true);
        Assert.Equal(GameChange.Unchanged, u.Change);
        Assert.StartsWith("Partial frame ignored", u.Log[0]);
        Assert.Equal(4, rec.Moves.Count);
        Assert.Equal(GameChange.Unchanged, rec.Update(P(S4), true).Change);
        Assert.Empty(rec.Breaks);
    }

    [Fact]
    public void MarkerDoesNotFlipSettledGame()
    {
        var rec = Begin();
        rec.Update(P(S1), true);
        var u = rec.Update(P(S1), true, PieceColor.White);
        Assert.Equal(GameChange.Unchanged, u.Change);
        Assert.Empty(u.Log);
        Assert.Equal(PieceColor.Black, rec.Current!.Turn);
    }

    [Fact]
    public void TakebackIsNotReviewed()
    {
        var rec = Begin();
        foreach (var f in new[] { S1, S2, S3 }) rec.Update(P(f), true);
        var u = rec.Update(P(S2), true);
        Assert.Equal(GameChange.Takeback, u.Change);
        Assert.Empty(u.Reviewable);
        Assert.Equal(2, rec.Moves.Count);
        Assert.StartsWith("Take-back", u.Log[0]);
        var again = rec.Update(P(S3), true);
        Assert.Equal("g2g3", Assert.Single(again.Reviewable).Uci);
    }

    [Fact]
    public void BoardRectJitterIsAbsorbedAndRealMovesAreFollowed()
    {
        var st = new RectStabilizer();
        var first = st.Accept(new BoardRect(310, 140, 576));
        Assert.True(first.Changed);
        foreach (var (dx, dy, ds) in new[] { (1.0, 0.0, 0.0), (-2.0, 3.0, 1.0), (3.0, -3.0, -3.0), (0.4, 0.6, 2.0), (-1.0, 1.0, 5.0) })
        {
            var r = st.Accept(new BoardRect(310 + dx, 140 + dy, 576 + ds));
            Assert.False(r.Changed);
            Assert.Equal(first.Rect, r.Rect);
        }
        var moved = st.Accept(new BoardRect(350, 140, 576));
        Assert.True(moved.Changed);
        Assert.Equal(350, moved.Rect.X);
        Assert.Equal(640, st.Accept(new BoardRect(350, 140, 640)).Rect.Size);
        Assert.Equal(new BoardRect(10, 21, 101), new RectStabilizer().Accept(new BoardRect(10.4, 20.6, 100.5)).Rect);
    }
}
