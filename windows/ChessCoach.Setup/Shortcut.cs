using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ChessCoach.Setup;

[GeneratedComInterface]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(nint file, int max, nint data, uint flags);
    void GetIDList(out nint list);
    void SetIDList(nint list);
    void GetDescription(nint name, int max);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory(nint dir, int max);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
    void GetArguments(nint args, int max);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    void GetHotkey(out ushort hotkey);
    void SetHotkey(ushort hotkey);
    void GetShowCmd(out int command);
    void SetShowCmd(int command);
    void GetIconLocation(nint path, int max, out int index);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    void Resolve(nint window, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

[GeneratedComInterface]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid id);
    [PreserveSig]
    int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string file, int mode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string? file, [MarshalAs(UnmanagedType.Bool)] bool remember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
    void GetCurFile(out nint file);
}

internal static partial class Shortcut
{
    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint options);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    public static void Create(string path, string target, string workingDirectory)
    {
        CoInitializeEx(0, 2);
        var clsid = new Guid("00021401-0000-0000-C000-000000000046");
        var linkId = typeof(IShellLinkW).GUID;
        var fileId = typeof(IPersistFile).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 1, in linkId, out var instance));
        var wrappers = new StrategyBasedComWrappers();
        var link = (IShellLinkW)wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.None);
        link.SetPath(target);
        link.SetWorkingDirectory(workingDirectory);
        link.SetDescription(InstallLayout.ProductName);
        link.SetIconLocation(target, 0);
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(instance, in fileId, out var persist));
        var file = (IPersistFile)wrappers.GetOrCreateObjectForComInstance(persist, CreateObjectFlags.None);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        file.Save(path, true);
    }
}
