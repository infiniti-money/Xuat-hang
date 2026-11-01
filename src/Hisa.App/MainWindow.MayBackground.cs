using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Forms = System.Windows.Forms;

namespace Hisa.App;

public partial class MainWindow
{
    private Forms.NotifyIcon? _mayTray;
    private Forms.ContextMenuStrip? _mayTrayMenu;
    private bool _backgroundNotificationShown;

    /// <summary>
    /// Runs the already-open HISA window in the Windows notification area.
    /// Does NOT activate the microphone or ask Gemini to start listening.
    /// Only a previously consented, actively running hands-free session survives
    /// minimizing the window. Closing HISA ends that session as before.
    /// </summary>
    private void InitializeMayBackground()
    {
        _mayTrayMenu=new Forms.ContextMenuStrip();
        var restore=new Forms.ToolStripMenuItem("Mở HISA / Mây Mây");
        restore.Click+=(_,_)=>Dispatcher.BeginInvoke(new Action(RestoreMayWindow));
        var stop=new Forms.ToolStripMenuItem("⏹ Dừng micro Mây");
        stop.Click+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>
        {
            StopHandsFreeSession();
            StatusText.Text="Đã dừng Mây và tắt micro. HISA vẫn chạy nền.";
            UpdateMayTrayStatus();
        }));
        var exit=new Forms.ToolStripMenuItem("Thoát hẳn HISA");
        exit.Click+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>
        {
            // The same normal close path releases Excel and microphone handles.
            Close();
        }));
        _mayTrayMenu.Items.Add(restore);
        _mayTrayMenu.Items.Add(stop);
        _mayTrayMenu.Items.Add(new Forms.ToolStripSeparator());
        _mayTrayMenu.Items.Add(exit);
        _mayTray=new Forms.NotifyIcon
        {
            Icon=ReadMayTrayIcon(),
            Text="HISA · Mây Mây (micro đang tắt)",
            Visible=true,
            ContextMenuStrip=_mayTrayMenu
        };
        _mayTray.MouseDoubleClick+=(_,e)=>
        {
            if(e.Button==Forms.MouseButtons.Left)
                Dispatcher.BeginInvoke(new Action(RestoreMayWindow));
        };
        StateChanged+=(_,_)=>
        {
            if(WindowState!=WindowState.Minimized)return;
            // Hiding the Window does not change _page. The existing consented
            // hands-free session keeps working while the app is in the tray.
            ShowInTaskbar=false;
            Hide();
            UpdateMayTrayStatus();
            if(_geminiHandsFreeActive && !_backgroundNotificationShown)
            {
                _backgroundNotificationShown=true;
                _mayTray?.ShowBalloonTip(4500,"Mây Mây đang chạy nền",
                    "Micro vẫn nghe vì bạn đã bật rảnh tay. Nhấp chuột phải vào biểu tượng HISA để Dừng micro hoặc Thoát.",
                    Forms.ToolTipIcon.Info);
            }
        };
        Closed+=(_,_)=>DisposeMayBackground();
    }

    private static Icon ReadMayTrayIcon()
    {
        try
        {
            var path=Environment.ProcessPath;
            if(!string.IsNullOrEmpty(path))
            {
                using var associated=Icon.ExtractAssociatedIcon(path);
                if(associated!=null)return (Icon)associated.Clone();
            }
        }
        catch (Exception) { /* Use a neutral OS fallback; no image downloads. */ }
        return (Icon)SystemIcons.Application.Clone();
    }

    private void UpdateMayTrayStatus()
    {
        if(_mayTray==null)return;
        _mayTray.Text=_geminiHandsFreeActive
            ? "HISA · Mây Mây ĐANG NGHE MICRO"
            : "HISA · Mây Mây (micro đang tắt)";
    }

    private void RestoreMayWindow()
    {
        if(!IsLoaded)return;
        ShowInTaskbar=true;
        Show();
        WindowState=WindowState.Normal;
        Activate();
    }

    private void DisposeMayBackground()
    {
        if(_mayTray!=null)
        {
            _mayTray.Visible=false;
            var icon=_mayTray.Icon;
            _mayTray.Dispose();
            _mayTray=null;
            icon?.Dispose();
        }
        _mayTrayMenu?.Dispose();
        _mayTrayMenu=null;
    }
}
