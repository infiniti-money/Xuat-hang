namespace Hisa.App;

public sealed partial class WarehouseService
{
    /// <summary>Serializes restore against warehouse writes. Caller must restart HISA to reload state.</summary>
    public Task<string> RestoreBackupAsync(string zipPath,bool restoreApplicationData,bool restoreExcel)
    {
        return MutateAsync(()=>
        {
            if(_disposed)throw new ObjectDisposedException(nameof(WarehouseService));
            var safety=BackupRestore.Restore(zipPath,_data,BackupDirectory,Settings,
                restoreApplicationData,restoreExcel);
            Say("Đã phục hồi. Đóng và mở lại HISA để nạp dữ liệu từ bản sao lưu.");
            return safety;
        });
    }
}
