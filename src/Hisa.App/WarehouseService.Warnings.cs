namespace Hisa.App;

public sealed partial class WarehouseService
{
    /// <summary>Uses indexed data and current history; does not read or write Excel.</summary>
    public SlipWarningReport CheckSlipWarnings(string contractText, string excludeSlipId = "", IReadOnlyList<ContractRow>? projectTargets = null)
    {
        var contracts = Rules.Tokens(contractText);
        if (contracts.Count == 0) return new SlipWarningReport { DataAvailable = _dataAvailable };
        var wanted = contracts.Select(Rules.Contract).ToHashSet();
        var targets = projectTargets?.Where(x => wanted.Contains(Rules.Contract(x.Contract)))
            .Select(x => new ContractRow { Contract = x.Contract, Project = x.Project, Company = x.Company }).DistinctBy(x => x.Identity).ToList() ?? [];
        var search = Search(new SearchFilter { ContractsText = string.Join("\n", contracts), ProjectTargets = targets });
        var result = new SlipWarningReport { RequestedCount = contracts.Count, DataAvailable = search.DataAvailable, Warnings = search.Warnings.Distinct().ToList() };
        var currentSlips = Slips.ToDictionary(x => x.Id, x => x, StringComparer.Ordinal);
        var targetCodes = targets.Select(x => Rules.Contract(x.Contract)).ToHashSet();
        var missingWithoutProjectTargets = search.Missing.Where(x => !targetCodes.Contains(Rules.Contract(x.Contract))).ToList();
        var rows = search.Rows.Concat(search.NoMaterialMatch).Concat(missingWithoutProjectTargets).DistinctBy(x => x.Identity).ToList();

        void Add(string code, ContractRow row, string message, string slipId = "", string slipCode = "", string day = "", string source = "", string reason = "")
        {
            if (slipId.Length > 0 && slipId == excludeSlipId) return;
            result.Items.Add(new SlipWarning
            {
                Code = code, Contract = row.Contract, Project = row.Project, Company = row.Company,
                Message = message, SlipId = slipId, SlipCode = slipCode, Date = day, Source = source, Reason = reason
            });
        }

        if (!search.DataAvailable)
            foreach (var contract in contracts)
                Add("unverified", new ContractRow { Contract = contract }, "Chưa xác nhận được dữ liệu chính. Đợi cập nhật Tổng hợp.xlsx trước khi kết luận mã thiếu hoặc lập phiếu xuất.", source: "Tổng hợp.xlsx");
        else
        {
            foreach (var row in missingWithoutProjectTargets)
                Add("missing", row, "Không có dữ liệu kiện Hưng Yên của hợp đồng trong Tổng hợp.xlsx.", source: "Tổng hợp.xlsx");
        }
        foreach (var target in targets.Where(t => !rows.Any(r => r.Identity == t.Identity)))
        {
            var missing = new ContractRow { Contract = target.Contract, Project = target.Project, Company = target.Company };
            // A matching number at another project must never satisfy an explicitly selected project.
            if (search.DataAvailable) Add("missing", missing, "Không có kiện Hưng Yên khớp công trình / công ty đã chọn. Cùng số hợp đồng có thể có dữ liệu ở công trình khác.", source: "Tổng hợp.xlsx");
            AttachHistory(missing, Slips, Flags, Shipments);
            rows.Add(missing);
        }

        foreach (var row in rows)
        {
            foreach (var slip in row.Slips.Where(s => s.Id != excludeSlipId))
            {
                var reference = string.IsNullOrWhiteSpace(slip.SlipCode) ? "chưa có mã" : slip.SlipCode;
                switch (slip.Status)
                {
                    case "delivered":
                        Add("shipped", row, $"Đã xuất hàng theo phiếu {reference}, ngày {Rules.Day(slip.HandoverDate)}.", slip.Id, slip.SlipCode, slip.HandoverDate, "Phiếu bàn giao", slip.Note);
                        Add("handed_over", row, $"Đã ghi nhận bàn giao theo phiếu {reference} (trạng thái Đã xuất), ngày {Rules.Day(slip.HandoverDate)}.", slip.Id, slip.SlipCode, slip.HandoverDate, "Phiếu bàn giao", slip.Note);
                        break;
                    case "delayed":
                        Add("delayed", row, $"Phiếu bàn giao {reference} đang tạm hoãn / chờ xuất.", slip.Id, slip.SlipCode, slip.HandoverDate, "Phiếu bàn giao", slip.DelayReason);
                        break;
                    case "cancelled":
                        Add("cancelled", row, $"Phiếu bàn giao {reference} đã hủy; kiểm tra lý do trước khi lập lại.", slip.Id, slip.SlipCode, slip.HandoverDate, "Phiếu bàn giao", slip.CancelReason);
                        break;
                    default:
                        Add("existing_slip", row, $"Đã có phiếu bàn giao {reference} · Chờ xuất; chưa ghi nhận xuất kho.", slip.Id, slip.SlipCode, slip.HandoverDate, "Phiếu bàn giao", slip.Note);
                        break;
                }
            }

            foreach (var flag in row.Flags)
            {
                if (flag.SlipId.Length > 0)
                {
                    if (flag.SlipId == excludeSlipId) continue;
                    // Generated flags are subordinate to their slip's CURRENT status.
                    if (currentSlips.TryGetValue(flag.SlipId, out var linked))
                    {
                        if (linked.Status != "delayed") continue;
                        // The linked slip has already supplied the detailed warning above.
                        if (row.Slips.Any(s => s.Id == linked.Id)) continue;
                    }
                }
                if (flag.Status == "delayed")
                    Add("delayed", row, "Hợp đồng đang tạm hoãn lấy hàng.", flag.SlipId, day: flag.UpdatedAt, source: "Trạng thái hợp đồng", reason: flag.Note);
                else if (flag.Status == "cancel_pickup")
                    Add("cancelled", row, "Hợp đồng đã hủy lấy hàng; kiểm tra lý do trước khi xuất.", flag.SlipId, day: flag.UpdatedAt, source: "Trạng thái hợp đồng", reason: flag.Note);
            }

            foreach (var shipment in row.Shipments)
            {
                if (shipment.SlipId.Length > 0 && shipment.SlipId == excludeSlipId) continue;
                // App-owned canonical rows are represented by the current slip above.
                if (shipment.SlipId.Length > 0 && currentSlips.ContainsKey(shipment.SlipId)) continue;
                if (shipment.SlipCode.Length > 0 && currentSlips.Values.Any(s => s.SlipCode == shipment.SlipCode)) continue;
                Add("shipped", row, $"Đã có lịch sử xuất hàng ngày {Rules.Day(shipment.Date)} trong {shipment.Source}.", shipment.SlipId, shipment.SlipCode, shipment.Date, shipment.Source, shipment.Note);
                if (Rules.Code(shipment.Source + " " + shipment.Note).Contains("BANGIAO"))
                    Add("handed_over", row, $"Có lịch sử bàn giao ngày {Rules.Day(shipment.Date)} trong {shipment.Source}.", shipment.SlipId, shipment.SlipCode, shipment.Date, shipment.Source, shipment.Note);
            }
        }
        result.Items = result.Items.DistinctBy(x => $"{x.Code}|{Rules.Identity(x.Contract, x.Project, x.Company)}|{x.SlipId}|{x.SlipCode}|{x.Date}|{x.Source}|{x.Reason}")
            .OrderBy(x => Array.IndexOf(new[] { "unverified", "missing", "delayed", "shipped", "handed_over", "existing_slip", "cancelled" }, x.Code))
            .ThenBy(x => Rules.Contract(x.Contract)).ThenBy(x => x.Project).ThenByDescending(x => x.Date).ToList();
        return result;
    }
}
