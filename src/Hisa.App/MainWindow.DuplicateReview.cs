using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

public partial class MainWindow
{
    private void ShowDuplicateReview()
    {
        _page="duplicate-review";
        var p=Stack();
        p.Children.Add(Alert("LƯU Ý: Nhiều kiện cùng số hợp đồng có thể là các kiện Hưng Yên khác nhau. " +
            "Hệ thống KHÔNG tự gộp hoặc xóa theo số hợp đồng. Chỉ dùng chức năng này khi đã kiểm tra kiện nguồn và xác định dữ liệu bị nhập sai."));
        var duplicateQuery=Input();duplicateQuery.Width=340;
        var duplicatePackages=PackageGrid([]);duplicatePackages.Height=260;var duplicateCount=T("Đang quét hợp đồng trùng…",12,true,Muted);
        void loadDuplicateGroups()
        {
            var key=Rules.Contract(duplicateQuery.Text);var all=_service.Snapshot.ContPackages;
            var groups=all.GroupBy(x=>Rules.Contract(x.Contract)).Where(g=>g.Count()>1).ToList();
            var repeated=groups.Where(g=>g.Key.Contains(key)).SelectMany(g=>g).OrderBy(x=>x.Contract).ThenBy(x=>x.Project).ThenBy(x=>x.PackageNo).ToList();
            duplicatePackages.ItemsSource=repeated;
            duplicateCount.Text=$"Tìm thấy {groups.Count:N0} số hợp đồng có nhiều kiện · hiển thị {repeated.Count:N0} kiện cùng số HĐ. Đây KHÔNG phải kết luận nhập trùng.";
        }
        var duplicateTools=Card(Stack(T("Rà soát kiện có cùng số hợp đồng",16,true),T("Cùng số hợp đồng KHÔNG có nghĩa kiện bị trùng. Cần phân biệt cont đi, ngày xuất cont, tên T1/T2, số lượng và vật tư. Chỉ xóa sau khi xác minh đây thực sự là dòng nhập sai.",12,false,Muted),Row(Field("Lọc số hợp đồng",duplicateQuery),B("⌕  Lọc",loadDuplicateGroups,true),A("⌫  Xóa kiện nhập sai (cần xác nhận)",async()=>{if(duplicatePackages.SelectedItem is not Package selected)throw new InvalidOperationException("Chọn kiện cần xóa.");if(!Ask($"XÓA KIỆN NGUỒN – KIỂM TRA LẠI THÔNG TIN\n\nSố HĐ: {selected.Contract}\nCông trình: {selected.Project}\nĐơn vị: {selected.Company}\nKiện số: {selected.PackageNo}\nNgày cont đi: {Rules.Day(selected.SourceDate)}\nSố cont: {(selected.ContainerNumber.Length>0?selected.ContainerNumber:"Chưa cập nhật")}\nVật tư: {selected.Items.Count} dòng\n\nChỉ chọn CÓ nếu xác minh đúng bản ghi nhập sai. Hai kiện cùng số HĐ nhưng khác ngày cont, số lượng hoặc T1/T2 phải giữ riêng.\n\nXóa sẽ tác động trang Cont đi Hưng Yên trong Tổng hợp.xlsx."))return;var n=await _service.DeleteContPackageAsync(selected);Notify($"Đã xóa kiện trùng ({n} dòng) khỏi Tổng hợp.xlsx.");loadDuplicateGroups();},false,true)),duplicateCount,duplicatePackages));
        duplicateQuery.TextChanged+=(_,_)=>loadDuplicateGroups();loadDuplicateGroups();
        p.Children.Add(duplicateTools);
        SetPage("Rà soát kiện cùng số hợp đồng",
            "Đối chiếu từng kiện trước khi xóa · không xem trùng số HĐ là trùng hàng",
            new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
}
