using System.Runtime.InteropServices;

namespace ChessCoach.Setup;

internal sealed record DialogButton(int Id, string Text);

internal sealed class DialogPage
{
    public required string Heading { get; init; }
    public string Body { get; init; } = "";
    public nint Icon { get; init; }
    public DialogButton[] Buttons { get; init; } = [];
    public int CommonButtons { get; init; }
    public string? Option { get; init; }
    public bool Progress { get; init; }
    public Action<TaskDialog>? Shown { get; init; }
    public Func<TaskDialog, int, bool>? Clicked { get; init; }
}

internal sealed unsafe partial class TaskDialog
{
    public const nint InformationIcon = 0xFFFD;
    public const nint ErrorIcon = 0xFFFE;
    public const int CloseButton = 0x20;
    public const int CancelButton = 0x8;

    private const uint UserMessage = 0x400;
    private const uint Navigate = UserMessage + 101;
    private const uint ProgressRange = UserMessage + 105;
    private const uint ProgressPosition = UserMessage + 106;
    private const uint Created = 0;
    private const uint Navigated = 1;
    private const uint ButtonClicked = 2;
    private const uint OptionClicked = 8;
    private const uint AllowCancellation = 0x8;
    private const uint ShowProgress = 0x200;
    private const uint UseHandleIcon = 0x2;

    private delegate int Callback(nint window, uint message, nint wParam, nint lParam, nint data);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Config
    {
        public uint Size;
        public nint Parent;
        public nint Instance;
        public uint Flags;
        public int CommonButtons;
        public nint Title;
        public nint MainIcon;
        public nint Heading;
        public nint Content;
        public uint ButtonCount;
        public nint Buttons;
        public int DefaultButton;
        public uint RadioCount;
        public nint Radios;
        public int DefaultRadio;
        public nint Option;
        public nint ExpandedText;
        public nint ExpandedLabel;
        public nint CollapsedLabel;
        public nint FooterIcon;
        public nint Footer;
        public nint Callback;
        public nint CallbackData;
        public uint Width;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Button
    {
        public int Id;
        public nint Text;
    }

    [LibraryImport("comctl32.dll")]
    private static partial int TaskDialogIndirect(ref Config config, out int button, nint radio, out int option);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ExtractIcon(nint instance, string file, uint index);

    private readonly string _title;
    private readonly nint _icon;
    private readonly Callback _callback;
    private readonly List<nint> _allocations = [];
    private DialogPage _page;
    private nint _window;

    public TaskDialog(string title, DialogPage first)
    {
        _title = title;
        _page = first;
        _callback = OnNotify;
        _icon = Environment.ProcessPath is { } path ? ExtractIcon(0, path, 0) : 0;
    }

    public bool OptionChecked { get; private set; }

    public int Show()
    {
        var config = Build(_page);
        TaskDialogIndirect(ref config, out var button, 0, out _);
        foreach (var block in _allocations) Marshal.FreeHGlobal(block);
        _allocations.Clear();
        return button;
    }

    public void GoTo(DialogPage page)
    {
        _page = page;
        var config = (Config*)Alloc(sizeof(Config));
        *config = Build(page);
        PostMessage(_window, Navigate, 0, (nint)config);
    }

    public void SetProgress(int percent) => PostMessage(_window, ProgressPosition, percent, 0);

    private nint Alloc(int size)
    {
        var block = Marshal.AllocHGlobal(size);
        lock (_allocations) _allocations.Add(block);
        return block;
    }

    private nint Text(string? text)
    {
        if (text == null) return 0;
        var block = Marshal.StringToHGlobalUni(text);
        lock (_allocations) _allocations.Add(block);
        return block;
    }

    private Config Build(DialogPage page)
    {
        var buttons = (Button*)Alloc(Math.Max(1, page.Buttons.Length) * sizeof(Button));
        for (var i = 0; i < page.Buttons.Length; i++) buttons[i] = new Button { Id = page.Buttons[i].Id, Text = Text(page.Buttons[i].Text) };
        var flags = AllowCancellation;
        if (page.Progress) flags |= ShowProgress;
        var icon = page.Icon;
        if (_icon != 0 && page.Icon == InformationIcon) { flags |= UseHandleIcon; icon = _icon; }
        return new Config
        {
            Size = (uint)sizeof(Config),
            Flags = flags,
            CommonButtons = page.CommonButtons,
            Title = Text(_title),
            MainIcon = icon,
            Heading = Text(page.Heading),
            Content = Text(page.Body),
            ButtonCount = (uint)page.Buttons.Length,
            Buttons = (nint)buttons,
            DefaultButton = page.Buttons.Length > 0 ? page.Buttons[0].Id : 0,
            Option = Text(page.Option),
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
        };
    }

    private int OnNotify(nint window, uint message, nint wParam, nint lParam, nint data)
    {
        switch (message)
        {
            case Created:
            case Navigated:
                _window = window;
                if (_page.Progress) PostMessage(window, ProgressRange, 0, 100 << 16);
                _page.Shown?.Invoke(this);
                break;
            case OptionClicked:
                OptionChecked = wParam != 0;
                break;
            case ButtonClicked:
                return _page.Clicked?.Invoke(this, (int)wParam) == true ? 1 : 0;
        }
        return 0;
    }
}
