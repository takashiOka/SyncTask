using SyncTask.Services;

namespace SyncTask;

public class FileSaveService : IFileSaveService
{
    public async Task<bool> SaveFileAsync(string defaultFileName, byte[] contents)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var filePath = Path.Combine(documents, defaultFileName);
        await File.WriteAllBytesAsync(filePath, contents);
        return true;
    }
}
