namespace SyncTask.Services;

public interface IFileSaveService
{
    /// <summary>
    /// ファイル保存ダイアログを開き、指定されたバイト列をファイルに保存する。
    /// </summary>
    /// <param name="defaultFileName">ダイアログに表示するデフォルトのファイル名。</param>
    /// <param name="contents">保存するバイト列。</param>
    /// <returns>保存に成功した場合は true。キャンセルまたは失敗した場合は false。</returns>
    Task<bool> SaveFileAsync(string defaultFileName, byte[] contents);
}
