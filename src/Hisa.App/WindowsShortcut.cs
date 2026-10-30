using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Hisa.App;

/// <summary>Unicode Windows shortcut creation, including Vietnamese OneDrive paths.</summary>
public static class WindowsShortcut
{
    [ComImport,Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int count,IntPtr data,int flags);
        void GetIDList(out IntPtr value);
        void SetIDList(IntPtr value);
        void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder value,int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder value,int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder value,int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetHotkey(out short value);
        void SetHotkey(short value);
        void GetShowCmd(out int value);
        void SetShowCmd(int value);
        void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int count,out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path,int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path,int reserved);
        void Resolve(IntPtr owner,int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
    public static void Create(string filename,string executable)
    {
        var link=(IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(executable);link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(executable)!);
            link.SetDescription(SoftwareInfo.DisplayName+" · "+SoftwareInfo.Creator);link.SetIconLocation(executable,0);link.SetShowCmd(1);
            ((IPersistFile)link).Save(filename,true);
        }
        finally{Marshal.FinalReleaseComObject(link);}
    }
}
