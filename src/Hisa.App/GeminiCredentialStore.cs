using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Hisa.App;

/// <summary>
/// Windows Credential Manager stores this secret in the user's Windows profile, not in
/// the application directory or Documents. Fixed target deliberately survives upgrades.
/// The secret is never included in JSON, Excel, ZIP backups, crash logs or GitHub releases.
/// </summary>
public sealed class GeminiCredentialStore(string? target = null)
{
    public const string CredentialTarget = "HISA.KhoVatTu.Gemini.API";
    private readonly string _target = string.IsNullOrWhiteSpace(target) ? CredentialTarget : target;
    private const uint Generic = 1;
    private const uint LocalMachine = 2;
    private const int NotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);

    public bool HasKey() => ReadKey() is { Length: > 0 };

    public string? ReadKey()
    {
        if (!CredRead(_target, Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw Failure("đọc", error);
        }
        try
        {
            var saved = Marshal.PtrToStructure<Credential>(pointer);
            if (saved.CredentialBlobSize == 0) return null;
            if (saved.CredentialBlob == IntPtr.Zero || saved.CredentialBlobSize % 2 != 0 ||
                saved.CredentialBlobSize > 4096)
                throw new InvalidOperationException("Khóa Gemini đã lưu không đúng định dạng. Vui lòng lưu lại trong Cấu hình.");
            return Marshal.PtrToStringUni(saved.CredentialBlob, (int)saved.CredentialBlobSize / 2);
        }
        finally { CredFree(pointer); }
    }

    public void SaveKey(string key)
    {
        key = key.Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Nhập Gemini API Key trước khi lưu.");
        if (key.Length > 1024 || key.Contains('\0'))
            throw new InvalidOperationException("Gemini API Key không đúng định dạng.");
        var bytes = Encoding.Unicode.GetBytes(key);
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new Credential
            {
                Type = Generic,
                TargetName = _target,
                UserName = Environment.UserName,
                CredentialBlob = pointer,
                CredentialBlobSize = (uint)bytes.Length,
                Persist = LocalMachine,
                Comment = "HISA Gemini API credential - retained on application update"
            };
            if (!CredWrite(ref credential, 0))
                throw Failure("lưu", Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, pointer, bytes.Length);
            Marshal.FreeHGlobal(pointer);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void DeleteKey()
    {
        if (!CredDelete(_target, Generic, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw Failure("xóa", error);
        }
    }

    private static Exception Failure(string operation, int error) =>
        new InvalidOperationException("Không thể " + operation +
            " Gemini API Key trong Windows Credential Manager (mã Windows " +
            error + "). Kiểm tra tài khoản Windows và quyền lưu thông tin đăng nhập.");
}
