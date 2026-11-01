using System.Globalization;

namespace Hisa.App;

public partial class MainWindow
{
    /// <summary>
    /// Resolve an EXPLICIT, local user request. If no contract was spoken,
    /// only approved scheduled dispatches ON THAT DATE may supply candidates.
    /// Untrusted Gemini output or warehouse notes never supply commands.
    /// This method NEVER creates or saves a slip.
    /// </summary>
    private MaySlipProposal? ResolveMaySlipProposal(MaySlipProposal request)
    {
        if(request.Contracts.Count>0)return request;
        // An empty contract list may mean "Mây lập phiếu xuất lịch ngày mai".
        // Only previously approved schedules with a matching dispatch day count.
        var schedules=AiScheduleWorkspace.RecentApproved(_service.DataDirectory);
        var candidates=schedules.SelectMany(d=>(d.Dispatches??[])
            .Where(x=>(string.IsNullOrWhiteSpace(x.PlannedDate)?
                d.DispatchDate:x.PlannedDate)==request.Day && !x.NeedsReview)
            .Select(x=>Rules.Contract(x.Contract)))
            .Where(x=>x.Length>=6 && x.Length<=20)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if(candidates.Count==0 || candidates.Count>MaySlipProposal.MaximumContracts)
            return null;
        return new MaySlipProposal(candidates,request.Day);
    }

    /// <summary>
    /// Local, review-only handoff. Does not call SaveSlipAsync, SavePlanAsync,
    /// ChangeSlipStatusAsync, sync Excel or approve a shipment.
    /// The normal HISA editor still requires a human click and validation.
    /// </summary>
    private void OpenMaySlipReview(MaySlipProposal proposal)
    {
        if(proposal.Contracts.Count==0 ||
           proposal.Contracts.Count>MaySlipProposal.MaximumContracts)
        {
            Notify("Mây cần từ 1 đến 10 số hợp đồng hợp lệ để soạn phiếu.");
            return;
        }
        if(!DateTime.TryParseExact(proposal.Day,"yyyy-MM-dd",
            CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
        {
            Notify("Ngày dự kiến xuất không hợp lệ. Mây chưa tạo phiếu.");
            return;
        }
        // Always search HISA's actual loaded materials at review time, not
        // the AI transcript or previously cached model response.
        var report=_service.Search(new SearchFilter
        {
            ContractsText=string.Join("\n",proposal.Contracts)
        });
        if(!report.DataAvailable)
        {
            Notify("Chưa nạp được Tổng hợp.xlsx. Vui lòng đồng bộ dữ liệu kho rồi nhờ Mây thử lại.");
            return;
        }
        if(report.Missing.Count>0)
        {
            Notify("Mây chưa tìm thấy kiện/vật tư nguồn của HĐ "+
                string.Join(", ",report.Missing.Select(x=>x.Contract))+
                ". Chưa tạo phiếu thiếu dữ liệu. Hãy kiểm tra nguồn Excel.");
            return;
        }
        if(!report.Rows.Any()&&!report.NoMaterialMatch.Any())
        {
            Notify("Mây không tìm thấy hợp đồng có dữ liệu hợp lệ. Chưa tạo phiếu.");
            return;
        }

        // No application mutation: CreateDraft returns a local in-memory
        // object. The existing UI requires human review to save as planned.
        var draft=_service.CreateDraft(report,
            _service.Templates.FirstOrDefault()?.Id??"internal_summary",
            proposal.Day);
        draft.Status="planned";
        // The operator requested the WHOLE contract/package. Include every
        // source material line by default. The editor remains available to
        // review and adjust lines BEFORE any saved or delivered status.
        foreach(var line in draft.Lines)line.Include=true;
        // One selected contract includes ALL material lines from every
        // selected Hưng Yên package in the source report. This is a proposal
        // only. Machine serials are suggested strictly by exact contract,
        // project, company AND material, never inferred from a similar HĐ.
        var serials=MotorSerialAutoFill.Fill(draft,
            EquipmentListReader.Load(_service.Settings).Rows,_service.Slips);
        if(draft.MotorSerials.Count>0)
            draft.Note+=" Đã gợi ý "+serials.Assigned+" số serial máy kéo từ MÁY KÉO.xlsx. "+
                (serials.Missing>0?
                    "Còn "+serials.Missing+" máy cần kiểm tra trước khi in và xuất hàng. ":"")+
                "Serial từng máy được in trên phiếu xuất/bàn giao khi đã đối chiếu.";
        draft.Note=(draft.Note.Length>0?draft.Note+" · ":"")+
            "Mây Mây đề xuất phiếu CHỜ XUẤT; chờ người sử dụng kiểm tra, duyệt và lưu.";
        var ambiguous=report.Rows.Concat(report.NoMaterialMatch)
            .GroupBy(x=>Rules.Contract(x.Contract))
            .Any(g=>g.Count()>1 || g.Any(x=>x.PackageCount>1));
        if(ambiguous)
            draft.Note+=" Có nhiều kiện/công trình; phải đối chiếu từng dòng trước khi duyệt.";
        draft.Note+=" Đã đưa "+draft.Lines.Count+" dòng vật tư từ "+
            draft.ContractPackages.Count+" kiện nguồn vào đề xuất. "+
            "Lịch dự kiến chưa tính là đã xuất; ngày thực tế chỉ ghi sau khi duyệt phiếu Đã xuất.";
        _draft=draft;
        _draftReport=report;
        _aiDispatchDay=proposal.Day;
        _createPlanMode=false;
        Navigate("create");
        // Status remains 'planned', not delivered, even if user saves later.
        // Existing ConfirmSlipWarnings, serial checks and delivered-specific
        // approval gates are untouched.
    }
}
