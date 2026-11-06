namespace Hisa.App;

/// <summary>
/// Source identity for motor serials. Contract is the lookup key in column D,
/// but a contract may cover several projects in column B. Never route serials
/// between different projects (or companies) merely because D matches.
/// </summary>
public static class MotorSerialProjectScope
{
    public sealed record Selection(List<EquipmentListReader.Row> Rows,
        string? Warning, bool MultipleProjects);

    public static List<string> Projects(IEnumerable<EquipmentListReader.Row> rows,
        string contract) =>
        rows.Where(x=>x.Category=="Máy kéo" &&
            Rules.Contract(x.Contract)==Rules.Contract(contract))
            .Select(x=>x.Project.Trim())
            .Where(x=>x.Length>0)
            .DistinctBy(Rules.Code)
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static Selection Resolve(IEnumerable<EquipmentListReader.Row> rows,
        string contract,string project,string company)
    {
        var matching=rows.Where(x=>x.Category=="Máy kéo" &&
            Rules.Contract(x.Contract)==Rules.Contract(contract)).ToList();
        if(matching.Count==0)
            return new([], "HĐ "+contract+": không tìm thấy máy kéo trong cột D của MÁY KÉO.xlsx.",false);

        var projects=Projects(matching,contract);
        var multiple=projects.Count>1;
        if(multiple && string.IsNullOrWhiteSpace(project))
            return new([], "HĐ "+contract+" có nhiều công trình: "+
                string.Join(", ",projects)+". Chọn công trình trước khi nhập serial.",true);

        if(projects.Count>0)
        {
            var selected=projects.FirstOrDefault(x=>
                Rules.Code(x)==Rules.Code(project));
            if(selected==null)
                return new([], "HĐ "+contract+" có công trình trong Excel là "+
                    string.Join(", ",projects)+", nhưng phiếu đang ghi '"+project+
                    "'. Không tự gán serial sang công trình khác.",multiple);
            matching=matching.Where(x=>Rules.Code(x.Project)==Rules.Code(selected)).ToList();
        }
        else if(matching.Count>1)
        {
            return new([], "HĐ "+contract+" có nhiều dòng nhưng cột B (công trình) trống. "+
                "Cần điền công trình vào Excel để tránh ghép nhầm serial.",false);
        }

        // Some production workbooks reuse a contract and project for more than
        // one customer. Require an exact company only when needed to separate.
        var companies=matching.Select(x=>x.Company.Trim())
            .Where(x=>x.Length>0).DistinctBy(Rules.Code).ToList();
        if(companies.Count>1)
        {
            if(string.IsNullOrWhiteSpace(company))
                return new([], "HĐ "+contract+" · "+project+
                    " thuộc nhiều công ty. Cần chọn đúng công ty trên phiếu.",multiple);
            matching=matching.Where(x=>Rules.Code(x.Company)==Rules.Code(company)).ToList();
            if(matching.Count==0)
                return new([], "HĐ "+contract+" · "+project+
                    ": công ty trên phiếu không khớp file MÁY KÉO.xlsx.",multiple);
        }
        return new(matching,null,multiple);
    }
}
