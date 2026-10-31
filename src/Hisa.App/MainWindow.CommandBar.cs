using System.Windows;

namespace Hisa.App;

public partial class MainWindow
{
    private void OpenUpdateClick(object sender, RoutedEventArgs e) => Navigate("information");
    private void OpenAiClick(object sender, RoutedEventArgs e) => Navigate("ai-schedule");
    private void OpenReadinessClick(object sender, RoutedEventArgs e) => Navigate("readiness");
}
