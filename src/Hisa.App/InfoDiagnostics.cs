using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hisa.App;

/// <summary>Offline STA checks; no app window or dialog is shown and only temporary files are used.</summary>
public static class InfoDiagnostics
{
    public static List<string> Run(string? imagePath=null)
    {
        var evidence=new List<string>();
        void Check(bool condition,string label)
        {
            if(!condition)throw new InvalidOperationException("FAIL: "+label);
            evidence.Add(label);
        }
        Check(SoftwareInfo.DisplayName=="Quản lý kho"&&SoftwareInfo.Creator=="Trịnh Phương Tân"&&SoftwareInfo.Phone=="0867899987"&&SoftwareInfo.Email=="infinitimoney@icloud.com","Exact software information and contact details");
        var root=SelfTests.CreateDemoWorkspace();
        using var service=new WarehouseService(root);
        var window=new MainWindow(service);
        try
        {
            typeof(MainWindow).GetMethod("ShowInformation",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            var updateButton=(Button)window.FindName("TopUpdateButton")!;
            Check(updateButton!=null && updateButton.Content?.ToString()?.Contains("Cập nhật")==true,
                "Update action stays visible in the main command bar");
            var texts=new List<string>();CollectText(window,texts);
            Check(new[]{SoftwareInfo.DisplayName,SoftwareInfo.Creator,SoftwareInfo.Phone,SoftwareInfo.Email}.All(value=>texts.Contains(value)),"About screen contains every requested visible field");
            var host=(Grid)typeof(MainWindow).GetField("PageHost",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.GetValue(window)!;
            var page=(FrameworkElement)host.Children[0];host.Children.Remove(page);
            var surface=new Border{Background=new SolidColorBrush(Color.FromRgb(243,246,250)),Padding=new Thickness(24),Child=page,Resources=window.Resources};
            TextElement.SetFontFamily(surface,new FontFamily("Segoe UI"));
            surface.Measure(new Size(1160,800));surface.Arrange(new Rect(0,0,1160,800));surface.UpdateLayout();
            Check(surface.ActualWidth==1160&&surface.ActualHeight==800,"About page lays out without showing a window");
            var bitmap=new RenderTargetBitmap(1160,800,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
            imagePath??=Path.Combine(Path.GetTempPath(),"hisa-native-software-information.png");
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var stream=File.Create(imagePath))encoder.Save(stream);
            Check(new FileInfo(imagePath).Length>4000,"About screen rendered to offline PNG");

            var first=MainWindow.CreateNewPrintTemplateModel();var second=MainWindow.CreateNewPrintTemplateModel();
            Check(first.Id!=second.Id&&first.Id!="internal_summary"&&second.Id!="internal_summary","New templates receive separate identifiers and do not overwrite defaults");
            Check(MainWindow.CreateNewPrintTemplateModel("contract_delivery").Layout=="contract_delivery"&&MainWindow.CreateNewPrintTemplateModel("standard").Layout=="standard","New template factory supports all three print layouts");
            var editor=new TemplateEditorWindow(first);
            try
            {
                var controls=(Dictionary<string,FrameworkElement>)typeof(TemplateEditorWindow).GetField("controls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
                Check(controls[nameof(PrintTemplate.Layout)] is ComboBox combo&&combo.Items.Count==3,"Template editor exposes three selectable print layouts");
                ((TextBox)controls[nameof(PrintTemplate.Name)]).Text="Mẫu kiểm tra chỉnh sửa";
                ((TextBox)controls[nameof(PrintTemplate.Title)]).Text="TIÊU ĐỀ ĐÃ SỬA";
                var edited=(PrintTemplate)typeof(TemplateEditorWindow).GetMethod("Read",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null)!;
                Check(edited.Name=="Mẫu kiểm tra chỉnh sửa"&&edited.Title=="TIÊU ĐỀ ĐÃ SỬA"&&first.Name!=edited.Name&&first.Title!=edited.Title,"Template edits preserve the original until saved");
            }
            finally{editor.Close();}
        }
        finally{window.Close();}
        return evidence;
    }
    private static void CollectText(DependencyObject parent,List<string> texts)
    {
        if(parent is TextBlock block)texts.Add(block.Text);
        if(parent is TextBox box)texts.Add(box.Text);
        foreach(var child in LogicalTreeHelper.GetChildren(parent))if(child is DependencyObject item)CollectText(item,texts);
    }
}
