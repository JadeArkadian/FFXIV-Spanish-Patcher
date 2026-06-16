namespace FFXIVSpanishPatcher.App.Services;

/// <summary>
/// Window-level interactions the view model needs but cannot do itself (file pickers, clipboard,
/// opening a folder). Implemented by the main window so the view model stays UI-toolkit-agnostic
/// and testable.
/// </summary>
public interface IShellServices
{
    /// <summary>Shows a folder picker for the FFXIV install; returns null if cancelled.</summary>
    Task<string?> PickGameFolderAsync();

    /// <summary>Copies text to the system clipboard.</summary>
    Task CopyToClipboardAsync(string text);

    /// <summary>Opens a folder in the OS file manager.</summary>
    void RevealInFileManager(string path);
}
