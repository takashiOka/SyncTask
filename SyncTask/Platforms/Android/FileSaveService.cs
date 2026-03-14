using SyncTask.Services;

namespace SyncTask;

public class FileSaveService : IFileSaveService
{
    public async Task<bool> SaveFileAsync(string defaultFileName, byte[] contents)
    {
        var filePath = Path.Combine(FileSystem.AppDataDirectory, defaultFileName);
        await File.WriteAllBytesAsync(filePath, contents);
        return true;
    }
}
