using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using SyncTask.Services;

namespace SyncTask;

public class FileSaveService : IFileSaveService
{
    public async Task<bool> SaveFileAsync(string defaultFileName, byte[] contents)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = defaultFileName
        };
        picker.FileTypeChoices.Add("Excel ファイル", new List<string> { ".xlsx" });

        var hwnd = GetMainWindowHandle();
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return false;
        }

        await FileIO.WriteBytesAsync(file, contents);
        return true;
    }

    private static IntPtr GetMainWindowHandle()
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
        {
            return WindowNative.GetWindowHandle(nativeWindow);
        }

        return IntPtr.Zero;
    }
}
