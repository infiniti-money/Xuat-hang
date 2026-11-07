namespace Hisa.App;

/// <summary>
/// Contract in column D is the motor serial lookup key. With exactly one
/// source project, ignore the project label on the slip; when a contract has
/// multiple projects in column B, a destination project must be resolved.
/// Never borrow serials from a different contract or an unconfirmed project.
/// </summary>
public static class MotorSerialProjectScope
{
    public sealed record Selection(List<EquipmentListReader.Row> Rows,
        string? Warning, bool MultipleProjects);

    // Stable selection key for one slip project, used only in the current
    // explicitly confirmed serial editing session.
    public static string SlipKey(string contract,string project,string company)=>
        Rules.Contract(contract)+"|"+Rules.Code(project)+"|"+Rules.Code(company);

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
        string contract,string project,string company,
        string? explicitlySelectedSourceProject=null)
    {
        var matching=rows.Where(x=>x.Category=="Máy kéo" &&
            Rules.Contract(x.Contract)==Rules.Contract(contract)).ToList();
        if(matching.Count==0)
            return new([], "HĐ "+contract+": không tìm thấy máy kéo trong cột D của MÁY KÉO.xlsx.",false);

        var projects=Projects(matching,contract);
        var multiple=projects.Count>1;
        // One contract with exactly one identifiable project needs only the
        // contract in column D. A different, missing or OCR-altered project
        // label on the slip is not an additional lookup requirement.
        if(projects.Count==1)
        {
            var onlyProject=projects[0];
            if(matching.Any(x=>string.IsNullOrWhiteSpace(x.Project)))
                return new([], "HĐ "+contract+" có dòng chưa ghi công trình ở cột B. "+
                    "Điền công trình vào dòng còn trống trước khi gán serial.",false);
            matching=matching.Where(x=>
                Rules.Code(x.Project)==Rules.Code(onlyProject)).ToList();
        }
        else if(multiple)
        {
            // Multiple source projects are genuinely different destinations.
            // Use the slip project for read-only lookup; an explicitly chosen
            // source project takes priority when the user confirms an I write.
            var destination=explicitlySelectedSourceProject??project;
            if(string.IsNullOrWhiteSpace(destination))
                return new([], "HĐ "+contract+" có nhiều công trình: "+
                    string.Join(", ",projects)+". Chọn công trình trước khi nhập serial.",true);
            if(!projects.Any(x=>Rules.Code(x)==Rules.Code(destination)))
                return new([], "HĐ "+contract+" có nhiều công trình "+
                    string.Join(", ",projects)+". Cần xác nhận công trình đích "+
                    "trước khi đồng bộ cột I.",true);
            matching=matching.Where(x=>
                Rules.Code(x.Project)==Rules.Code(destination)).ToList();
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
