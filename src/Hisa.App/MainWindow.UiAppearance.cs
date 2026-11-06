using System.Windows;
using System.Windows.Media;

namespace Hisa.App;

public partial class MainWindow
{
    private bool _darkAppearance;

    private void InitializeAppearance()
    {
        _darkAppearance=UiAppearancePreferences.Read().DarkMode;
        ApplyAppearance();
    }

    private void ToggleAppearanceClick(object sender,RoutedEventArgs e)
    {
        var newMode=!_darkAppearance;
        try{new UiAppearancePreferences{DarkMode=newMode}.Save();}
        catch(Exception ex)
        {
            MessageBox.Show(this,"Không thể lưu chế độ giao diện: "+ex.Message,
                "HISA · Giao diện",MessageBoxButton.OK,MessageBoxImage.Warning);
            return;
        }
        _darkAppearance=newMode;
        ApplyAppearance();
        StatusText.Text=_darkAppearance
            ?"Đã chọn giao diện tối. Có thể chuyển lại bằng nút ☀ Sáng."
            :"Đã chọn giao diện sáng. Có thể chuyển lại bằng nút ☾ Tối.";
    }

    private void ApplyAppearance()
    {
        // UI-only state. No Excel file, slip, microphone consent or API key is touched.
        var palette=_darkAppearance
            ? new Dictionary<string,string>
            {
                ["Line"]="#40566A",["Accent"]="#36B9BB",
                ["SoftAccent"]="#194444",["PanelBackground"]="#1B3041",
                ["PageBackground"]="#10202E",["ControlBackground"]="#162A3B",
                ["GridAlternate"]="#20394A",["GridHeader"]="#284355",
                ["GridSelected"]="#285E64",["GridSelectedText"]="#FFFFFF",
                ["HeaderMeta"]="#B1C7D7"
            }
            : new Dictionary<string,string>
            {
                ["Line"]="#DEE7EF",["Accent"]="#158E95",
                ["SoftAccent"]="#E7F6F4",["PanelBackground"]="#FFFFFF",
                ["PageBackground"]="#F4F7FB",["ControlBackground"]="#FFFFFF",
                ["GridAlternate"]="#F7FAFC",["GridHeader"]="#EAF1F6",
                ["GridSelected"]="#D9EFF0",["GridSelectedText"]="#164E58",
                ["HeaderMeta"]="#688095"
            };

        foreach(var (name,color) in palette)
            Resources[name]=Brush(color);

        Ink.Color=(Color)ColorConverter.ConvertFromString(
            _darkAppearance?"#E9F4FA":"#18354D");
        Muted.Color=(Color)ColorConverter.ConvertFromString(
            _darkAppearance?"#B1C7D7":"#70869B");
        CardSurface.Color=(Color)ColorConverter.ConvertFromString(
            _darkAppearance?"#1B3041":"#FFFFFF");
        CardStroke.Color=(Color)ColorConverter.ConvertFromString(
            _darkAppearance?"#40566A":"#DFE7EF");
        Resources["Ink"]=Ink;
        Resources["Muted"]=Muted;
        TopAppearanceButton.Content=_darkAppearance?"☀ Sáng":"☾ Tối";
        TopAppearanceButton.ToolTip=_darkAppearance
            ?"Chuyển về giao diện sáng":"Chuyển sang giao diện tối";
    }
}
