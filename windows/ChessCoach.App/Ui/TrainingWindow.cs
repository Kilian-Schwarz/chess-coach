using System.Windows;
using System.Windows.Controls;
using ChessCoach.App.Model;
using ChessCoach.Core;

namespace ChessCoach.App.Ui;

internal sealed class TrainingWindow
{
    private readonly AppModel _model;
    private readonly Window _window = Kit.Window(Loc.T("training.title"), 760, 600);
    private readonly BoardView _board = new() { Width = 460, Height = 460, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _progress = Kit.Text("", 13, true, bottom: 6);
    private readonly TextBlock _info = Kit.Text("", 14, bottom: 8);
    private readonly TextBlock _hint = Kit.Text("", 13, muted: true, bottom: 8);
    private readonly Button _hintBtn, _solutionBtn, _nextBtn;
    private List<Puzzle> _queue = new();
    private int _index;
    private Puzzle? _puzzle;
    private ChessPosition? _position;
    private PlayerProfile? _profile;
    private int _hintLevel;
    private bool _recorded, _finished;
    private (int File, int Rank)? _selected;

    public TrainingWindow(AppModel model)
    {
        _model = model;
        _hintBtn = Kit.Btn(Loc.T("training.hint"), NextHint);
        _solutionBtn = Kit.Btn(Loc.T("training.solution"), ShowSolution);
        _nextBtn = Kit.Btn(Loc.T("training.next"), Next);
        var side = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        side.Children.Add(_progress); side.Children.Add(_info); side.Children.Add(_hint);
        var row = new WrapPanel();
        row.Children.Add(_hintBtn); row.Children.Add(_solutionBtn); row.Children.Add(_nextBtn);
        side.Children.Add(row);
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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
        _queue = PuzzleScheduler.TodaysSelection(set, _profile.FocusSkill);
        _index = -1;
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
            return;
        }
        _puzzle = _queue[_index];
        _position = ChessPosition.FromFen(_puzzle.Fen);
        _hintLevel = 0; _recorded = false; _finished = false; _selected = null;
        if (_position == null) { Next(); return; }
        _board.Set(_position.Board, _position.Turn == PieceColor.White);
        _progress.Text = Loc.T("training.progress", _index + 1, _queue.Count);
        _info.Text = Loc.T("training.find", _position.Turn.Label());
        _hint.Text = "";
        SetButtons(true);
    }

    private void SetButtons(bool active)
    {
        _hintBtn.IsEnabled = active; _solutionBtn.IsEnabled = active;
        _nextBtn.IsEnabled = _queue.Count > 0;
    }

    private void OnDown(int f, int r)
    {
        if (_puzzle == null || _position == null || _finished) return;
        if (_position.Board.Squares[r][f] is char p && Placement.ColorOf(p) == _position.Turn)
        {
            _selected = (f, r);
            _board.Select(_selected);
        }
        else if (_selected is { } from) TryMove(from, (f, r));
    }

    private void OnUp(int f, int r)
    {
        if (_puzzle == null || _position == null || _finished || _selected is not { } from) return;
        if (from != (f, r)) TryMove(from, (f, r));
    }

    private void TryMove((int File, int Rank) from, (int File, int Rank) to)
    {
        if (_position == null || _puzzle == null) return;
        var move = _position.LegalMoves().FirstOrDefault(m => m.FromFile == from.File && m.FromRank == from.Rank && m.ToFile == to.File && m.ToRank == to.Rank && (m.Promotion == null || m.Promotion == 'q'));
        if (move.Equals(default(UciMove))) { _selected = null; _board.Select(null); return; }
        bool correct = _puzzle.Accepts(move.Uci);
        if (!_recorded)
        {
            _recorded = true;
            Record(correct);
        }
        _selected = null;
        _board.Select(null);
        if (correct)
        {
            _finished = true;
            _info.Text = Loc.T("training.correct", _position.Board.DescribeMove(move));
            _board.Mark(from, to);
            SetButtons(false);
        }
        else _info.Text = Loc.T("training.wrong");
    }

    private void Record(bool correct)
    {
        if (_puzzle == null || _profile == null) return;
        _puzzle.Attempt(_puzzle.AcceptedMoves.FirstOrDefault() is { } best && correct ? best : "0000", _hintLevel, null, _profile.LearningSpeed);
        _model.PuzzleAttempted(_profile, _puzzle, correct);
        _profile.LearningSpeed = PuzzleScheduler.AdjustedLearningSpeed(_profile.LearningSpeed, _model.PuzzlesFor(_profile.Id).Puzzles);
        _model.SavePuzzles(_profile.Id);
        _model.SaveProfile(_profile);
    }

    private void NextHint()
    {
        if (_puzzle == null || _position == null || _finished) return;
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
        if (_puzzle == null || _position == null || UciMove.Parse(_puzzle.AcceptedMoves[0]) is not { } m) return;
        if (!_recorded) { _recorded = true; _hintLevel = 4; Record(false); }
        _finished = true;
        _board.Mark((m.FromFile, m.FromRank), (m.ToFile, m.ToRank));
        _hint.Text = Loc.T("training.best", _position.Board.DescribeMove(m));
        _info.Text = "";
        SetButtons(false);
    }
}
