using System.IO;

namespace Hisa.App;

public sealed partial class WarehouseService
{
    /// <summary>
    /// Save the single selected work status and synchronize the ORIGINAL
    /// MÁY KÉO.xlsx under the same warehouse write gate as slip exports.
    /// Never confirm a delivery in this workflow.
    /// </summary>
    public Task<MotorHandoverExcel.Outcome> SetMotorWorkAsync(
        MotorHungYenStore.Item item,bool completed,string actualDay,string note)
    {
        return MutateAsync(()=>
        {
            var path=Settings.TractionMachineListPath;
            if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
                throw new FileNotFoundException("Chưa có file MÁY KÉO.xlsx trong Kho HISA.",path);
            var rows=EquipmentListReader.Read(path,"Máy kéo");
            var identity=MotorHandoverExcel.IdentityFor(item);
            if(string.IsNullOrWhiteSpace(item.Serial) || rows.Count(row=>
                MotorHandoverExcel.IdentityFor(new MotorHungYenStore.Item
                {
                    Contract=row.Contract,Project=row.Project,Company=row.Company,
                    Serial=row.Serial
                })==identity)!=1)
                throw new InvalidOperationException(
                    "Máy kéo/seri không khớp duy nhất trong Excel. Kiểm tra HĐ, công trình và đơn vị.");
            if(MotorHandoverExcel.ConfirmedDates(_slips).ContainsKey(identity))
                throw new InvalidOperationException(
                    "Máy đã bàn giao theo phiếu Đã xuất. Chỉ sửa ngày bàn giao trên phiếu.");
            // An Excel-entered dispatch date is protected even if an HISA slip
            // has not yet been linked to this serial. Never replace it with text.
            var original=rows.Single(row=>
                MotorHandoverExcel.IdentityFor(new MotorHungYenStore.Item
                {
                    Contract=row.Contract,Project=row.Project,Company=row.Company,
                    Serial=row.Serial
                })==identity);
            if(DateTime.TryParseExact(original.MotorColumnK,"yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,out _))
                throw new InvalidOperationException(
                    "Cột K đã có ngày xuất kho. Không ghi đè bằng Chưa làm / Đã làm; cần đối chiếu phiếu đã xuất.");
            var tracker=new MotorHungYenStore(_data);
            // A changed original source row is never accepted as a new identity.
            if(!tracker.List(rows).Any(x=>x.Key==item.Key && x.PresentInSource &&
                MotorHandoverExcel.IdentityFor(x)==identity))
                throw new InvalidOperationException("Danh sách máy kéo đã thay đổi. Hãy làm mới trước khi chọn.");
            tracker.SetWorkStatus(item,completed,actualDay,note);
            try
            {
                return MotorHandoverExcel.Sync(Settings,_slips,_data,
                    Path.Combine(BackupDirectory,"Excel"),identity);
            }
            catch(Exception)
            {
                // JSON state remains safe even if Excel is temporarily open.
                // The background export reconciliation can retry this write.
                _exportsPending=true;
                throw;
            }
        });
    }
}
