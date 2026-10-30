using System.IO;
using System.Text.Json;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>Warning regressions use their own temporary workspace and never touch user workbooks.</summary>
public static class SlipWarningDiagnostics
{
    public static async Task<List<string>> RunAsync()
    {
        var root = SelfTests.CreateDemoWorkspace();
        var data = Path.Combine(root, "native_data");
        var checks = new List<string>();
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + message);
            checks.Add("PASS: " + message);
        }
        Slip Fixture(string id, string status, string contract, string project) => new()
        {
            Id = id, SlipCode = id.ToUpperInvariant(), Status = status, HandoverDate = "2026-10-27", Contracts = [contract],
            Lines = [new MaterialLine { Contract = contract, Project = project, Company = "Công ty A", Material = "Máy kéo", Unit = "Bộ", Quantity = 1 }],
            DelayReason = status == "delayed" ? "Khách chưa nhận hàng" : ""
        };
        var slips = JsonSerializer.Deserialize<List<Slip>>(File.ReadAllText(Path.Combine(data, "slips.json")), WarehouseService.Json)!;
        slips.Add(Fixture("warn-delivery", "delivered", "05492026", "Công trình B"));
        slips.Add(Fixture("warn-delay", "delayed", "20012026", "Công trình C"));
        slips.Add(Fixture("missing-delivered", "delivered", "77772026", "Công trình chưa có dữ liệu"));
        File.WriteAllText(Path.Combine(data, "slips.json"), JsonSerializer.Serialize(slips, WarehouseService.Json));
        var flags = new List<ContractFlag>
        {
            new() { Contract="05492026",Project="Công trình B",Company="Công ty A",Status="delayed",SlipId="warn-delivery",Note="Cờ hoãn cũ đã hết hiệu lực" },
            new() { Contract="05492026",Project="Công trình A",Company="Công ty A",Status="delayed",SlipId="fixture-pending",Note="Cờ tự sinh không còn đúng trạng thái" },
            new() { Contract="123(1)",Project="Công trình riêng (1)",Company="Công ty A",Status="delayed",Note="Chờ khách" },
            new() { Contract="1231",Project="Công trình khác",Company="Công ty A",Status="cancel_pickup",Note="Khách hủy" }
        };
        File.WriteAllText(Path.Combine(data, "contract_flags.json"), JsonSerializer.Serialize(flags, WarehouseService.Json));
        using (var wb = new XLWorkbook(Path.Combine(root, "Thành phẩm xuất kho.xlsx")))
        {
            var ws = wb.Worksheet(1); ws.Cell(4, 2).Value = "Công trình C"; ws.Cell(4, 3).Value = "Công ty A";
            ws.Cell(4, 4).Value = "20012026"; ws.Cell(4, 5).Value = new DateTime(2026, 10, 27); ws.Cell(4,5).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Cell(4, 6).Value = "HISA Slip ID: fixture-cancel | Mã phiếu: TEST-CANCEL"; wb.Save();
        }
        using (var notLoaded = new WarehouseService(root))
        {
            var unavailable = notLoaded.CheckSlipWarnings("05492026\n99992026");
            Check(!unavailable.DataAvailable && unavailable.UnverifiedCount == 2 && unavailable.MissingCount == 0, "Chưa tải nguồn không báo sai mã không có dữ liệu");
        }
        using var service = new WarehouseService(root);
        await service.InitializeAsync();
        Check(service.LastError.Length == 0, "Nạp dữ liệu tạm cho kiểm tra cảnh báo");
        var empty = service.CheckSlipWarnings("");
        Check(empty.RequestedCount == 0 && !empty.HasWarnings && empty.Items.Count == 0, "Không quét toàn bộ dữ liệu khi ô hợp đồng trống");
        var projectA = new ContractRow { Contract = "05492026", Project = "Công trình A", Company = "Công ty A" };
        var projectB = new ContractRow { Contract = "05492026", Project = "Công trình B", Company = "Công ty A" };
        var pending = service.CheckSlipWarnings("BG 05492026", projectTargets: [projectA]);
        Check(pending.ExistingSlipCount == 1 && pending.Items.Any(x => x.SlipId == "fixture-pending"), "Phiếu chờ hiện cảnh báo đã có phiếu bàn giao");
        Check(pending.ShippedCount == 0 && pending.HandedOverCount == 0, "Phiếu chờ hoặc đã in chưa được coi là xuất / bàn giao thực tế");
        Check(pending.DelayedCount == 0, "Cờ hoãn tự sinh cũ không ghi đè trạng thái phiếu hiện tại");
        var otherProject = service.CheckSlipWarnings("05492026", projectTargets: [projectB]);
        Check(otherProject.ShippedCount == 1 && otherProject.HandedOverCount == 1, "Phiếu đã xuất có cảnh báo xuất hàng và bàn giao");
        Check(otherProject.Items.All(x => x.Project == "Công trình B") && otherProject.ExistingSlipCount == 0, "Cảnh báo tách đúng công trình khi trùng số hợp đồng");
        Check(otherProject.DelayedCount == 0, "Bỏ cờ hoãn cũ của phiếu đã xuất");
        var selfPending = service.CheckSlipWarnings("05492026", "fixture-pending", [projectA]);
        Check(!selfPending.Items.Any(x => x.SlipId == "fixture-pending") && selfPending.ExistingSlipCount == 0, "Sửa phiếu không tự cảnh báo trùng chính phiếu đó");
        var selfDelivery = service.CheckSlipWarnings("05492026", "warn-delivery", [projectB]);
        Check(selfDelivery.ShippedCount == 0 && selfDelivery.HandedOverCount == 0, "Sửa phiếu loại cả dòng sổ xuất do chính phiếu ghi");
        var allProjects = service.CheckSlipWarnings("05492026");
        Check(allProjects.ExistingSlipCount == 1 && allProjects.ShippedCount == 1 && allProjects.Items.Any(x => x.Project == "Công trình A") && allProjects.Items.Any(x => x.Project == "Công trình B"), "Nhập chỉ số hợp đồng kiểm tra mọi công trình có cùng mã");
        var missing = service.CheckSlipWarnings("99992026");
        Check(missing.MissingCount == 1 && missing.ShippedCount == 1 && missing.HandedOverCount == 0, "Mã chưa có vật tư vẫn hiện độc lập lịch sử sổ thành phẩm");
        var historyOnly = service.CheckSlipWarnings("77772026");
        Check(historyOnly.MissingCount == 1 && historyOnly.ShippedCount == 1 && historyOnly.HandedOverCount == 1, "Lịch sử bàn giao không thay thế cảnh báo thiếu dữ liệu vật tư");
        var prefix = service.CheckSlipWarnings("0549");
        Check(prefix.MissingCount == 1 && prefix.ExistingSlipCount == 0 && prefix.ShippedCount == 0, "Không khớp nhầm tiền tố số hợp đồng");
        var suffix = service.CheckSlipWarnings("123(1)\n1231");
        Check(suffix.RequestedCount == 2 && suffix.Items.Any(x => x.Code == "delayed" && x.Contract == "123(1)") && suffix.Items.Any(x => x.Code == "cancelled" && x.Contract == "1231"), "Mã ngoặc và mã nối liền giữ cảnh báo riêng");
        var delayed = service.CheckSlipWarnings("20012026");
        Check(delayed.DelayedCount == 1 && delayed.Items.Any(x => x.Code == "delayed" && x.Reason == "Khách chưa nhận hàng"), "Cảnh báo tạm hoãn giữ mã phiếu và lý do");
        Check(delayed.CancelledCount == 1 && delayed.ShippedCount == 0, "Phiếu hủy và dòng sổ chưa dọn không bị tính là đã xuất");
        var wrongProject = service.CheckSlipWarnings("05492026", projectTargets: [new ContractRow { Contract="05492026",Project="Không tồn tại",Company="Công ty A" }]);
        Check(wrongProject.MissingCount == 1 && wrongProject.ShippedCount == 0 && wrongProject.ExistingSlipCount == 0, "Công trình chỉ định không có nguồn không mượn dữ liệu công trình khác");
        var wrongMissingProject = service.CheckSlipWarnings("77772026", projectTargets: [new ContractRow { Contract="77772026",Project="Dự án khác",Company="Công ty A" }]);
        Check(wrongMissingProject.MissingCount == 1 && wrongMissingProject.ShippedCount == 0 && wrongMissingProject.HandedOverCount == 0, "Mã chưa có nguồn vẫn giới hạn đúng công trình lịch sử khi được chỉ định");
        await service.ChangeSlipStatusAsync("warn-delivery", "cancelled", "Đổi kế hoạch trong kiểm tra");
        var afterCancel = service.CheckSlipWarnings("05492026", projectTargets: [projectB]);
        Check(afterCancel.CancelledCount == 1 && afterCancel.ShippedCount == 0 && afterCancel.HandedOverCount == 0, "Cảnh báo dùng trạng thái mới nhất sau khi hủy phiếu xuất");
        return checks;
    }
}
