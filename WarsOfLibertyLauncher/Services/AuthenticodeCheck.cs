using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Whether a downloaded program really is what it says, before it is run with administrator
/// rights. <c>CreateFromSignedFile</c> alone only reads the certificate: it does not notice a
/// file changed after signing, which is what <c>WinVerifyTrust</c> is for.
/// </summary>
internal static class AuthenticodeCheck
{
    /// <summary>The certificate that signed the file, or null when it is not validly signed.</summary>
    internal static string? TrustedSigner(string path)
    {
        try
        {
            if (Verify(path) != 0) return null;
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return certificate.Subject;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AuthenticodeCheck: '{path}' could not be read: {ex.Message}");
            return null;
        }
    }

    private static int Verify(string path)
    {
        var file = new WinTrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = path,
        };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,        // WTD_UI_NONE
                RevocationChecks = 0, // WTD_REVOKE_NONE: an offline CRL must not refuse a good file
                UnionChoice = 1,     // WTD_CHOICE_FILE
                FileInfo = filePointer,
                StateAction = 0,     // WTD_STATEACTION_IGNORE
                ProviderFlags = 0x40, // WTD_SAFER_FLAG
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // GENERIC_VERIFY_V2
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
