using System.Windows;
using System.Windows.Controls;
using ChessCoach.App.Model;
using ChessCoach.Core;

namespace ChessCoach.App.Ui;

internal sealed class TrainingWindow
{
    private readonly AppModel _model;
    private readonly Window _window = Kit.Window(Loc.T("training.title"), 860, 640);
    private readonly BoardView _board = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MinWidth = 320, MinHeight = 320 };
    private readonly TextBlock _progress = Kit.Text("", 13, true, bottom: 6);
    private readonly TextBlock _info = Kit.Text("", 14, bottom: 8);
    private readonly TextBlock _hint = Kit.Text("", 13, muted: true, bottom: 8);
    private readonly TextBlock _instruction = Kit.Text(Loc.T("training.instruction"), 13, muted: true, bottom: 10);
    private readonly TextBlock _stats = Kit.Text("", 12, muted: true);
    private readonly Button _hintBtn, _solutionBtn, _nextBtn;
    private List<Puzzle> _queue = new();
    private int _index;
    private Puzzle? _puzzle;
    private ChessPosition? _position;
    private PlayerProfile? _profile;
    private int _hintLevel;
    private bool _recorded, _finished;
    private (int File, int Rank)? _selected;
    private int _lineIndex, _solved, _attempted;
    private bool _busy, _whiteAtBottom = true, _nudged;
    private DateTime _shownAt = DateTime.UtcNow;
    private double _thinkSeconds = Curriculum.BaseThinkSeconds;

    public TrainingWindow(AppModel model)
    {
        _model = model;
        _hintBtn = Kit.Btn(Loc.T("training.hint"), NextHint);
        _solutionBtn = Kit.Btn(Loc.T("training.solution"), ShowSolution);
        _nextBtn = Kit.Btn(Loc.T("training.skip"), Next);
        _window.MinWidth = 700; _window.MinHeight = 520;
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(_hintBtn); buttons.Children.Add(_solutionBtn); buttons.Children.Add(_nextBtn);
        var top = new StackPanel();
        top.Children.Add(_progress); top.Children.Add(_instruction); top.Children.Add(_info); top.Children.Add(_hint);
        var side = new Grid { Margin = new Thickness(16, 0, 0, 0), Width = 300 };
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.Children.Add(top);
        Grid.SetRow(buttons, 1); side.Children.Add(buttons);
        Grid.SetRow(_stats, 3); side.Children.Add(_stats);
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_board);
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        _window.Content = grid;
        _board.SquareDown += OnDown;
        _board.SquareUp += OnUp;
    }

    public void Show()
    {
        _profile = _model.Active;
        var set = _model.PuzzlesFor(_profile.Id).Puzzles;
        var priority = _model.TakePriorityPuzzles();
        var adaptation = _model.AdaptationFor(_profile);
        _thinkSeconds = adaptation.ThinkSeconds;
        var first = priority.Select(id => set.FirstOrDefault(p => p.Id == id)).OfType<Puzzle>().ToList();
        _queue = first.Concat(PuzzleScheduler.TodaysSelection(set, _profile.FocusSkill, maxNew: adaptation.NewPuzzlesPerDay).Where(p => !first.Contains(p))).ToList();
        _index = -1; _solved = 0; _attempted = 0;
        Next();
        Kit.Open(_window);
    }

    private void Next()
    {
        if (_profile == null) return;
        _index++;
        if (_index >= _queue.Count)
        {
            _puzzle = null;
            _board.Set(new Placement(), true);
            _progress.Text = "";
            _info.Text = _queue.Count == 0 ? Loc.T("training.noneDue") : Loc.T("training.done");
            _hint.Text = "";
            SetButtons(false);
            ShowStats();
            return;
        }
        _puzzle = _queue[_index];
        _position = ChessPosition.FromFen(_puzzle.Fen);
        _hintLevel = 0; _recorded = false; _finished = false; _selected = null; _lineIndex = 0; _busy = false;
        _nudged = false; _shownAt = DateTime.UtcNow;
        if (_position == null) { Next(); return; }
        _whiteAtBottom = _position.Turn == PieceColor.White;
        _board.Set(_position.Board, _whiteAtBottom);
        _progress.Text = Loc.T("training.progress", _index + 1, _queue.Count);
        _info.Text = Loc.T("training.find", _position.Turn.Label());
        _hint.Text = "";
        SetButtons(true);
        ShowStats();
    }

    private void ShowStats() => _stats.Text = _queue.Count == 0 ? "" : Loc.T("training.stats", _solved, _attempted);

    private void SetButtons(bool active)
    {
        _hintBtn.IsEnabled = active; _solutionBtn.IsEnabled = active;
        _nextBtn.IsEnabled = _queue.Count > 0;
    }

    private bool Locked => _puzzle == null || _position == null || _finished || _busy;

    private void Select((int File, int Rank)? sq)
    {
        _selected = sq;
        _board.Select(sq, sq is { } s && _position != null
            ? _position.LegalMoves().Where(m => m.FromFile == s.File && m.FromRank == s.Rank).Select(m => (m.ToFile, m.ToRank)).Distinct()
            : null);
    }

    private void OnDown(int f, int r)
    {
        if (Locked) return;
        if (_position!.Board.Squares[r][f] is char p && Placement.ColorOf(p) == _position.Turn) Select((f, r));
        else if (_selected is { } from) TryMove(from, (f, r));
    }

    private void OnUp(int f, int r)
    {
        if (Locked || _selected is not { } from) return;
        if (from != (f, r)) TryMove(from, (f, r));
    }

    private void TryMove((int File, int Rank) from, (int File, int Rank) to)
    {
        var candidates = _position!.LegalMoves().Where(m => m.FromFile == from.File && m.FromRank == from.Rank && m.ToFile == to.File && m.ToRank == to.Rank).ToList();
        if (candidates.Count == 0) { Select(null); return; }
        if (candidates.Count == 1) { Play(candidates[0]); return; }
        var menu = new ContextMenu { PlacementTarget = _board, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        foreach (var m in candidates)
        {
            var move = m;
            var item = new MenuItem { Header = PromotionGlyph(move.Promotion), FontSize = 28 };
            item.Click += (_, _) => Play(move);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private static string PromotionGlyph(char? piece) => char.ToLowerInvariant(piece ?? 'q') switch { 'r' => "♖", 'b' => "♗", 'n' => "♘", _ => "♕" };

    private async void Play(UciMove move)
    {
        if (Locked) return;
        var from = (move.FromFile, move.FromRank);
        var to = (move.ToFile, move.ToRank);
        string uci = move.Uci;
        bool correct = _lineIndex == 0 ? _puzzle!.Accepts(uci) : _puzzle!.BestLine.Count > _lineIndex && uci == _puzzle.BestLine[_lineIndex];
        Select(null);
        string? line = null;
        if (!correct)
        {
            var check = await CheckAsync(uci);
            line = check?.Line;
            if (check != null && _lineIndex == 0 && !_recorded && _puzzle!.TryAcceptAlternative(uci, check.Loss)) correct = true;
        }
        if (!correct)
        {
            if (!_recorded) { _recorded = true; _attempted++; Record(false); ShowStats(); }
            _info.Text = line == null ? Loc.T("training.wrong") : Loc.T("training.wrong") + "\n" + Loc.T("training.after", line);
            return;
        }
        string described = _position!.Board.DescribeMove(move);
        bool onLine = _puzzle.BestLine.Count > 0 && uci == _puzzle.BestLine[0] || _lineIndex > 0;
        _position = _position.Applying(move);
        _board.Set(_position.Board, _whiteAtBottom);
        _board.Mark(from, to);
        int reply = _lineIndex + 1;
        if (onLine && reply + 1 < _puzzle.BestLine.Count && UciMove.Parse(_puzzle.BestLine[reply]) is { } answer && UciMove.Parse(_puzzle.BestLine[reply + 1]) != null)
        {
            _busy = true;
            _info.Text = Loc.T("training.correct", described);
            var current = _puzzle;
            await Task.Delay(500);
            _busy = false;
            if (_puzzle != current) return;
            _position = _position.Applying(answer);
            _board.Set(_position.Board, _whiteAtBottom);
            _board.Mark((answer.FromFile, answer.FromRank), (answer.ToFile, answer.ToRank));
            _lineIndex = reply + 1;
            return;
        }
        _finished = true;
        if (!_recorded) { _recorded = true; _attempted++; Record(true); }
        if (_hintLevel == 0) _solved++;
        ShowStats();
        _info.Text = Loc.T("training.correct", described);
        SetButtons(false);
    }

    private async Task<AppModel.MoveCheck?> CheckAsync(string uci)
    {
        if (_puzzle == null || _position == null) return null;
        var current = _puzzle;
        _busy = true;
        try
        {
            var check = await _model.CheckMoveAsync(_position.Fen, uci);
            return _puzzle == current ? check : null;
        }
        finally { _busy = false; }
    }

    private void Record(bool correct)
    {
        if (_puzzle == null || _profile == null) return;
        _puzzle.Attempt(_puzzle.AcceptedMoves.FirstOrDefault() is { } best && correct ? best : "0000", _hintLevel, null, _profile.LearningSpeed);
        _model.PuzzleAttempted(_profile, _puzzle, correct);
        _profile.AdjustLearningSpeed(_model.PuzzlesFor(_profile.Id).Puzzles);
        _model.SavePuzzles(_profile.Id);
        _model.SaveProfile(_profile);
    }

    private void NextHint()
    {
        if (_puzzle == null || _position == null || _finished) return;
        if (Curriculum.NeedsThinkDelay(_hintLevel, (DateTime.UtcNow - _shownAt).TotalSeconds, _thinkSeconds, _nudged))
        {
            _nudged = true;
            _hint.Text = Loc.T("hint.thinkFirst");
            return;
        }
        _hintLevel = Math.Min(4, _hintLevel + 1);
        var best = UciMove.Parse(_puzzle.AcceptedMoves[0]);
        if (best is not { } m) return;
        char piece = _position.Board.Squares[m.FromRank][m.FromFile] ?? 'P';
        HintThemeExt.TryParse(_puzzle.Theme, out var theme);
        switch (_hintLevel)
        {
            case 1: _hint.Text = theme.Stage1Text(); break;
            case 2:
                _hint.Text = Hints.Stage2Text(piece, UciMove.SquareName(m.FromFile, m.FromRank));
                _board.Mark((m.FromFile, m.FromRank), null);
                break;
            case 3: _hint.Text = Hints.Stage3Fallback(theme, piece); break;
            default: ShowSolution(); break;
        }
    }

    private void ShowSolution()
    {
        if (_puzzle == null || _position == null || _finished) return;
        string best = _lineIndex > 0 && _puzzle.BestLine.Count > _lineIndex ? _puzzle.BestLine[_lineIndex] : _puzzle.AcceptedMoves[0];
        if (UciMove.Parse(best) is not { } m) return;
        if (!_recorded) { _recorded = true; _attempted++; _hintLevel = 4; Record(false); ShowStats(); }
        _finished = true;
        Select(null);
        _board.Mark((m.FromFile, m.FromRank), (m.ToFile, m.ToRank));
        _hint.Text = Loc.T("training.best", _position.Board.DescribeMove(m));
        _info.Text = "";
        SetButtons(false);
    }
}
