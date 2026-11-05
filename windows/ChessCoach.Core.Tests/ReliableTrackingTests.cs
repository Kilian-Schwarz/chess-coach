using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class ReliableTrackingTests
{
    private sealed class Rig
    {
        public readonly string Dir = TestSupport.SyntheticDir()!;
        public readonly BoardRect Rect;
        public readonly PieceTemplates Templates = new();
        public readonly BoardColors Colors;
        public readonly int Margin, CropX, CropY;
        public readonly BoardRect Expected;

        public Rig()
        {
            var start = TestSupport.LoadPng(Path.Combine(Dir, "big_start.png"));
            Rect = BoardDetector.Detect(start)!.Value;
            var features = BoardScanner.Analyze(start, Rect, Templates).Features;
            Templates.LearnStart(features);
            Colors = BoardColors.Learn(features);
            Margin = (int)(Rect.Size * 0.2);
            CropX = (int)Rect.X - Margin;
            CropY = (int)Rect.Y - Margin;
            Expected = new BoardRect(Rect.X - CropX, Rect.Y - CropY, Rect.Size);
        }

        public RgbImage Crop(string name) => TestSupport.Crop(TestSupport.LoadPng(Path.Combine(Dir, name + ".png")), CropX, CropY, (int)Rect.Size + 2 * Margin, (int)Rect.Size + 2 * Margin);

        public RectStabilizer Tracker()
        {
            var st = new RectStabilizer();
            st.Accept(Rect);
            return st;
        }

        public RegionResult Observe(string name, RectStabilizer st, BoardColors? colors, bool useColors = true) =>
            RegionObserver.Observe(Crop(name), (CropX, CropY), Expected, Templates, useColors ? colors ?? Colors : null, st);
    }

    private static bool Kept(RegionResult r) => r switch
    {
        RegionResult.Scanned s => !s.Changed,
        RegionResult.Verified or RegionResult.Covered => true,
        _ => false,
    };

    [Fact]
    public void BoardIsKeptWhileAPieceIsDraggedOrMarked()
    {
        if (TestSupport.SyntheticDir() == null) return;
        var rig = new Rig();
        foreach (var name in new[] { "big_start", "big_mid", "big_drag", "big_dots", "big_arrows" })
        {
            var st = rig.Tracker();
            var r = rig.Observe(name, st, null);
            Assert.True(Kept(r), $"{name}: board lost instead of kept ({r})");
            Assert.Equal(rig.Tracker().Accepted, st.Accepted);
        }
    }

    [Fact]
    public void EmptyAreaFailsAndMissingColoursStillUseTheGrid()
    {
        if (TestSupport.SyntheticDir() == null) return;
        var rig = new Rig();
        int side = (int)rig.Rect.Size + 2 * rig.Margin;
        var blank = new RgbImage(side, side, Enumerable.Repeat((byte)40, side * side * 4).ToArray());
        Assert.IsType<RegionResult.Failed>(RegionObserver.Observe(blank, (rig.CropX, rig.CropY), rig.Expected, rig.Templates, rig.Colors, rig.Tracker()));
        Assert.True(Kept(rig.Observe("big_drag", rig.Tracker(), null, useColors: false)));
    }

    [Fact]
    public void TransientWrongRectangleNeverMovesTheBoard()
    {
        if (TestSupport.SyntheticDir() == null) return;
        var rig = new Rig();
        var st = rig.Tracker();
        var first = rig.Observe("big_shifted", st, null);
        Assert.Equal(rig.Tracker().Accepted, st.Accepted);
        if (first is RegionResult.Scanned s) Assert.False(s.Changed);
        rig.Observe("big_mid", st, null);
        rig.Observe("big_shifted", st, null);
        Assert.Equal(rig.Tracker().Accepted, st.Accepted);

        var other = rig.Tracker();
        for (int i = 0; i < 5; i++)
        {
            rig.Observe("big_other_theme", other, null);
            Assert.Equal(rig.Tracker().Accepted, other.Accepted);
        }

        var moved = rig.Tracker();
        rig.Observe("big_shifted", moved, null);
        Assert.Equal(rig.Tracker().Accepted, moved.Accepted);
        rig.Observe("big_shifted", moved, null);
        Assert.NotEqual(rig.Tracker().Accepted, moved.Accepted);

        var noColors = rig.Tracker();
        rig.Observe("big_shifted", noColors, null, useColors: false);
        rig.Observe("big_shifted", noColors, null, useColors: false);
        Assert.NotEqual(rig.Tracker().Accepted, noColors.Accepted);
    }

    [Fact]
    public void ReplayOfMissAndVerifyNeverFailsOrDrifts()
    {
        if (TestSupport.SyntheticDir() == null) return;
        var rig = new Rig();
        var st = rig.Tracker();
        foreach (var name in new[] { "big_mid", "big_drag", "big_drag", "big_dots", "big_dots", "big_arrows", "big_mid", "big_drag", "big_mid" })
            Assert.False(rig.Observe(name, st, null) is RegionResult.Failed, name);
        Assert.Equal(rig.Tracker().Accepted, st.Accepted);
    }

    [Fact]
    public void RectChangeNeedsTwoConsecutiveFramesAndMatchingColours()
    {
        var st = new RectStabilizer();
        var home = new BoardRect(300, 100, 560);
        Assert.Equal(RectOutcome.Changed, st.Propose(home, true).Outcome);
        var other = new BoardRect(340, 100, 560);
        Assert.Equal(RectOutcome.Pending, st.Propose(other, true).Outcome);
        Assert.Equal(home, st.Accepted);
        var moved = st.Propose(other, true);
        Assert.Equal(RectOutcome.Changed, moved.Outcome);
        Assert.Equal(340, st.Accepted!.Value.X);
    }

    [Fact]
    public void RectChangeWithWrongColoursOrAlternatingCandidatesIsRejected()
    {
        var st = new RectStabilizer();
        var home = new BoardRect(300, 100, 560);
        st.Propose(home, true);
        for (int i = 0; i < 4; i++) Assert.Equal(RectOutcome.Pending, st.Propose(new BoardRect(600, 300, 400), false).Outcome);
        Assert.Equal(RectOutcome.Changed, st.Propose(new BoardRect(600, 300, 400), true).Outcome);
        var again = new RectStabilizer();
        again.Propose(home, true);
        Assert.Equal(RectOutcome.Pending, again.Propose(new BoardRect(600, 300, 400), true).Outcome);
        Assert.Equal(RectOutcome.Pending, again.Propose(new BoardRect(100, 50, 300), true).Outcome);
        Assert.Equal(RectOutcome.Pending, again.Propose(new BoardRect(600, 300, 400), true).Outcome);
        Assert.Equal(home, again.Accepted);
    }

    [Fact]
    public void JitterWithinToleranceIsTheSameRect()
    {
        var st = new RectStabilizer();
        st.Propose(new BoardRect(300, 100, 560), true);
        Assert.Equal(RectOutcome.Same, st.Propose(new BoardRect(302, 99, 561), false).Outcome);
    }

    [Fact]
    public void RelearnNeedsThreeBadFramesAtOneRect()
    {
        var g = new RelearnGuard();
        var a = new BoardRect(100, 100, 800);
        var b = new BoardRect(101, 100, 800);
        Assert.False(g.Observe(true, a));
        Assert.False(g.Observe(true, a));
        Assert.True(g.Observe(true, a));
        g.Reset();
        Assert.False(g.Observe(true, a));
        Assert.False(g.Observe(true, b));
        Assert.False(g.Observe(true, a));
        g.Reset();
        g.Observe(true, a); g.Observe(true, a);
        Assert.False(g.Observe(false, a));
        Assert.False(g.Observe(true, a));
        Assert.True(g.Observe(true, a, 1));
    }

    [Fact]
    public void EarlyStartIsOnlyForFewTrackedPlies()
    {
        Assert.Equal(4, EarlyStart.MaxPlies);
    }

    [Fact]
    public void StuckRecoveryFiresAfterThreeSeconds()
    {
        var stuck = new StuckRecovery();
        var t = DateTime.UtcNow;
        Assert.False(stuck.Observe(true, t));
        Assert.False(stuck.Observe(true, t.AddSeconds(2.9)));
        Assert.False(stuck.Observe(false, t.AddSeconds(3)));
        Assert.False(stuck.Observe(true, t.AddSeconds(4)));
        Assert.True(stuck.Observe(true, t.AddSeconds(7.5)));
        Assert.False(stuck.Observe(true, t.AddSeconds(8)));
    }

    private const string LiftedFen = "rnbqkbnr/pp2pppp/8/2pp4/8/5NP1/PPPPPP1P/RNBQK2R w KQkq - 0 3";

    [Fact]
    public void ForcedUpdateResyncsWhereAPartialFrameIsIgnored()
    {
        var rec = new GameRecord();
        rec.Update(ChessPosition.Start.Board, true);
        var played = ChessPosition.Start;
        foreach (var uci in new[] { "g1f3", "d7d5", "g2g3", "c7c5" })
        {
            played = played.Applying(UciMove.Parse(uci)!.Value);
            rec.Update(played.Board, true);
        }
        Assert.Equal(4, rec.Moves.Count);
        var lifted = ChessPosition.FromFen(LiftedFen)!.Board;
        Assert.Equal(GameChange.Unchanged, rec.Update(lifted, true).Change);
        Assert.False(rec.Current!.Board.Equals(lifted));
        var forced = rec.Update(lifted, true, forced: true);
        Assert.NotEqual(GameChange.Unchanged, forced.Change);
        Assert.True(rec.Current!.Board.Equals(lifted));
    }

    [Fact]
    public void ResyncKeepsAllPliesAndRestoresAfterGarbage()
    {
        var rec = new GameRecord();
        var pos = ChessPosition.Start;
        rec.Update(pos.Board, true);
        for (int played = 0; played < 40; played++)
        {
            var m = pos.LegalMoves().First();
            pos = pos.Applying(m);
            rec.Update(pos.Board, true);
        }
        int before = rec.Moves.Count;
        Assert.True(before >= 30);
        string? startedWith = rec.StartPosition?.Fen;
        var odd = pos.Board.Clone();
        int swapped = 0;
        for (int r = 0; r < 8; r++)
            for (int f = 0; f < 8; f++)
                if (swapped < 4 && odd.Squares[r][f] != null && (r + f) % 3 == 0) { odd.Squares[r][f] = null; swapped++; }
        var u = rec.Update(odd, true);
        Assert.Equal(GameChange.Resync, u.Change);
        Assert.Equal(before, rec.Moves.Count);
        Assert.Equal(new[] { before }, rec.Breaks);
        Assert.Equal(startedWith, rec.StartPosition?.Fen);
        var back = rec.Update(pos.Board, true);
        Assert.Equal(before, rec.Moves.Count);
        Assert.NotEqual(GameChange.NewGame, back.Change);
    }

    [Fact]
    public void GarbageFrameAddsABreakAndTheOldPositionRemovesIt()
    {
        var rec = new GameRecord();
        var pos = ChessPosition.Start;
        rec.Update(pos.Board, true);
        for (int played = 0; played < 12; played++)
        {
            pos = pos.Applying(pos.LegalMoves().First());
            rec.Update(pos.Board, true);
        }
        var garbage = ChessPosition.FromFen("8/8/8/3k4/8/8/8/4K3 w - - 0 1")!.Board;
        Assert.Equal(GameChange.Resync, rec.Update(garbage, true).Change);
        Assert.Single(rec.Breaks);
        rec.Update(pos.Board, true);
        Assert.Empty(rec.Breaks);
        Assert.Equal(12, rec.Moves.Count);
    }

    [Fact]
    public void CoveredCountsOnlyForTheBoardWindowsOwnContent()
    {
        Assert.Equal(BoardSight.Covered, BoardReadability.Effective(BoardSight.Covered, true));
        Assert.Equal(BoardSight.Gone, BoardReadability.Effective(BoardSight.Covered, false));
        Assert.Equal(BoardSight.Readable, BoardReadability.Effective(BoardSight.Readable, false));
        Assert.Equal(BoardSight.Gone, BoardReadability.Effective(BoardSight.Gone, true));
    }

    [Fact]
    public void LogDirectoryHonoursTheEnvironmentOverride()
    {
        Assert.Equal(Path.Combine(TestSupport.Root, "build", "tests", "logs"), Environment.GetEnvironmentVariable("CHESSCOACH_LOG_DIR"));
        Assert.Equal("/custom/logs", Log.DefaultDirectory("/custom/logs"));
    }

    [Fact]
    public void UniformlyDarkenedPixelsAreMoveOverlays()
    {
        Assert.True(SquareFeature.IsMoveOverlay((200, 180, 150), (240, 217, 181)));
        Assert.True(SquareFeature.IsMoveOverlay((208, 187, 156), (240, 217, 181)));
        Assert.False(SquareFeature.IsMoveOverlay((240, 217, 181), (240, 217, 181)));
        Assert.False(SquareFeature.IsMoveOverlay((120, 109, 90), (240, 217, 181)));
        Assert.False(SquareFeature.IsMoveOverlay((200, 150, 150), (240, 217, 181)));
        Assert.False(SquareFeature.IsMoveOverlay((20, 20, 20), (30, 30, 30)));
    }

    private static char?[][] Screen(string rows) =>
        rows.Split('/').Select(row => row.SelectMany(ch => char.IsDigit(ch) ? new char?[ch - '0'] : new char?[] { ch }).ToArray()).ToArray();

    [Fact]
    public void OrientationIsStickyAndChangesOnlyOnStartOrImpossibleEvidence()
    {
        var start = Screen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR");
        var flipped = start.Reverse().Select(row => row.Reverse().ToArray()).ToArray();
        var t = new OrientationTracker();
        Assert.True(t.Resolve(true, start, true).WhiteAtBottom);
        Assert.True(t.WhiteAtBottom);
        for (int i = 0; i < 30; i++) Assert.True(t.Resolve(false, start, false).WhiteAtBottom);
        var seen = t.Resolve(false, flipped, true);
        Assert.False(seen.WhiteAtBottom);
        Assert.NotNull(seen.Reason);
    }

    [Fact]
    public void OrientationFlipsAfterFiveFramesOfImpossibleEvidence()
    {
        var mutualPawnChecks = Screen("8/8/8/4k3/3P4/8/3p4/4K3");
        var t = new OrientationTracker();
        t.Set(true);
        for (int i = 0; i < OrientationTracker.EvidenceFrames - 1; i++) Assert.Null(t.Resolve(true, mutualPawnChecks, false).Reason);
        var last = t.Resolve(true, mutualPawnChecks, false);
        Assert.False(last.WhiteAtBottom);
        Assert.NotNull(last.Reason);
    }

    private static string? Fixtures() => TestSupport.PrivateFixtures();

    [Fact]
    public void DragFixtureReadsPiecesUnderRingsAndDots()
    {
        if (Fixtures() is not { } dir || !File.Exists(Path.Combine(dir, "chesscom_drag_knight_e5_over_g4.png"))) return;
        var (templates, _) = TestSupport.Learn(Path.Combine(dir, "chesscom_start_white.png"));
        var res = BoardScanner.Analyze(TestSupport.LoadPng(Path.Combine(dir, "chesscom_drag_knight_e5_over_g4.png")), new BoardRect(18, 66, 1136), templates);
        Assert.Equal('n', res.Screen[2][2]);
        Assert.Equal('p', res.Screen[1][5]);
        Assert.Equal('b', res.Screen[1][4]);
        Assert.Equal('k', res.Screen[0][4]);
        foreach (var (r, c) in new[] { (1, 3), (4, 5), (5, 6) }) Assert.Null(res.Screen[r][c]);
    }

    [Fact]
    public void EndgameFixtureKeepsWhiteAtTheBottom()
    {
        if (Fixtures() is not { } dir || !File.Exists(Path.Combine(dir, "chesscom_endgame_flipped_orientation.png"))) return;
        var (templates, _) = TestSupport.Learn(Path.Combine(dir, "chesscom_start_white.png"));
        var res = BoardScanner.Scan(TestSupport.LoadPng(Path.Combine(dir, "chesscom_endgame_flipped_orientation.png")), templates)!;
        Assert.Equal("7r/5Kp1/8/p7/Pk1b2P1/1P6/8/8", TestSupport.PlacementOf(res.Screen, true));
        var tracker = new OrientationTracker();
        tracker.Set(true);
        for (int i = 0; i < 20; i++)
        {
            var r = tracker.Resolve(false, res.Screen, false);
            Assert.True(r.WhiteAtBottom);
            Assert.Null(r.Reason);
        }
    }

    [Fact]
    public void LiftFixturePositionMarksNothingForTheKnightOnE2()
    {
        var pos = ChessPosition.FromFen("4k3/8/8/3p3p/3P1B2/8/PPP1NPPP/R5K1 w - - 0 1")!;
        Assert.Empty(new LiftedPiece(4, 1, 'N').UnsafeSquares(pos));
    }
}
