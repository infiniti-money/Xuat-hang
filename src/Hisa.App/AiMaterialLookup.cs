namespace Hisa.App;

/// <summary>
/// Read-only, identity-safe material lookup for rows recognized in a Zalo image.
/// Gemini is never a source of quantities: material and receipt data are read
/// exclusively from the current canonical warehouse snapshot.
/// </summary>
public static class AiMaterialLookup
{
    public sealed class Row
    {
        public string Contract { get; set; } = "";
        public string Project { get; set; } = "";
        public string Company { get; set; } = "";
        public string PackageNo { get; set; } = "";
        public string SourceDate { get; set; } = "";
        public string ArrivalDate { get; set; } = "";
        public string Material { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal? Quantity { get; set; }
        public string Note { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public sealed record Result(List<Row> Items,string Message,bool Exact,bool CanVerify);

    public static Result ForContract(AiScheduleLine schedule,WorkbookSnapshot snapshot)
    {
        var code=Rules.Contract(schedule.Contract);
        if(code.Length==0)
            return Unmatched(schedule,"Thiếu số hợp đồng trong ảnh Zalo; cần sửa lại trước khi đối chiếu.");
        var all=snapshot.Packages.Count>0 ? snapshot.Packages :
            snapshot.ContPackages.Concat(snapshot.DetailPackages).ToList();
        var candidates=all.Where(x=>Rules.Contract(x.Contract)==code).ToList();
        if(candidates.Count==0)
            return Unmatched(schedule,"Chưa tìm thấy vật tư hợp đồng này trong Tổng hợp.xlsx. Không tự tạo danh sách hàng.");

        var identity=Rules.Identity(schedule.Contract,schedule.Project,schedule.Company);
        var exact=candidates.Where(x=>x.Identity==identity).ToList();
        bool verified=exact.Count>0 && !string.IsNullOrWhiteSpace(schedule.Project) &&
            !string.IsNullOrWhiteSpace(schedule.Company);
        string warning="";
        if(!verified)
        {
            // A matching contract alone is NOT enough when it belongs to T1/T2 or
            // different projects/companies: never silently mix their materials.
            var identities=candidates.Select(x=>x.Identity).Distinct(StringComparer.Ordinal).ToArray();
            if(identities.Length>1)
                return Unmatched(schedule,"Hợp đồng này có nhiều công trình/đơn vị trong Excel. Hãy sửa tên công trình và đơn vị ở dòng ảnh để đối chiếu chính xác; không lấy gộp vật tư.");
            exact=candidates;
            warning="Số hợp đồng khớp nhưng công trình/đơn vị trên ảnh chưa khớp hoàn toàn với Excel. Danh sách dưới đây chỉ để tham khảo; sửa dòng ảnh trước khi lập phiếu hoặc ghi nhập kho.";
        }
        var rows=exact.SelectMany(p=>p.Items.Where(x=>x.IsHungYen).Select(material=>new Row
        {
            Contract=p.Contract,Project=p.Project,Company=p.Company,
            PackageNo=p.PackageNo.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SourceDate=p.SourceDate,ArrivalDate=p.ArrivalDate,
            Material=material.Material,Unit=material.Unit,Quantity=material.Quantity,
            Note=material.Note,Status=p.ArrivalDate.Length>0?"Đã ghi ngày về":"Chưa ghi ngày về"
        })).ToList();
        if(rows.Count==0)
            return Unmatched(schedule,"Đã nhận diện hợp đồng nhưng chưa tìm thấy dòng vật tư Hưng Yên hợp lệ.");
        return new Result(rows,verified
            ? rows.Count+" dòng vật tư từ Tổng hợp.xlsx. Chỉ xem/đối chiếu; không ghi kho."
            : warning,verified,verified);
    }

    private static Result Unmatched(AiScheduleLine schedule,string reason)=>new(
        [new Row{Contract=schedule.Contract,Project=schedule.Project,Company=schedule.Company,
            Material="—",Status="Cần đối chiếu",Note=reason}],reason,false,false);

    public static List<Row> ForAll(IEnumerable<AiScheduleLine> schedules,WorkbookSnapshot snapshot)=>
        schedules.SelectMany(x=>ForContract(x,snapshot).Items).ToList();
}
