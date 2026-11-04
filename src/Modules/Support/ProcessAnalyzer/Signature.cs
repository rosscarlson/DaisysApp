using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>A file's Authenticode signature: signed (in the file, or in one of Windows' catalogs as Windows' own files
/// are), not signed, or signed but not trusted (changed since, or a certificate Windows doesn't trust).</summary>
public enum SignState { Unknown, Signed, Catalog, Unsigned, Untrusted }

/// <summary>
/// Checks a file's signature with Windows' WinVerifyTrust, as Explorer's Digital Signatures tab does: the file's own
/// signature, or (Windows' own files) its hash in a system catalog. Offline (no revocation check). Who signed it is
/// the signing certificate's name.
/// </summary>
internal static class Signature
{
    public static (SignState State, string Signer) Check(string path)
    {
        try
        {
            int r = VerifyFile(path);
            if (r == 0) return (SignState.Signed, SignerOf(path));
            if (r != TRUST_E_NOSIGNATURE && r != TRUST_E_SUBJECT_FORM_UNKNOWN && r != TRUST_E_PROVIDER_UNKNOWN) return (SignState.Untrusted, SignerOf(path));
            var (cr, catalog) = VerifyCatalog(path);
            if (cr == 0)
            {
                string s = catalog != null ? SignerOf(catalog) : "";
                return (SignState.Catalog, s.Length > 0 ? s : "Microsoft Windows");
            }
            return (SignState.Unsigned, "");
        }
        catch { return (SignState.Unknown, ""); }
    }

    private static string SignerOf(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var c = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return c.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch { return ""; }
    }

    private static int VerifyFile(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var info = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = pathPtr };
        var infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(info, infoPtr, false);
            return Verify(WTD_CHOICE_FILE, infoPtr, GenericVerifyV2);
        }
        finally { Marshal.FreeHGlobal(infoPtr); Marshal.FreeHGlobal(pathPtr); }
    }

    /// <summary>The file's hash looked for in Windows' catalogs (SHA-256, then SHA-1 for older ones), and checked there.</summary>
    private static (int Result, string? Catalog) VerifyCatalog(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        foreach (var alg in new[] { "SHA256", "SHA1" })
        {
            if (!CryptCATAdminAcquireContext2(out var admin, IntPtr.Zero, alg, IntPtr.Zero, 0)) continue;
            try
            {
                int size = 64;
                var hash = new byte[size];
                if (!CryptCATAdminCalcHashFromFileHandle2(admin, fs.SafeFileHandle.DangerousGetHandle(), ref size, hash, 0)) continue;
                IntPtr prev = IntPtr.Zero;
                var cat = CryptCATAdminEnumCatalogFromHash(admin, hash, size, 0, ref prev);
                if (cat == IntPtr.Zero) continue;
                try
                {
                    var ci = new CATALOG_INFO { cbStruct = (uint)Marshal.SizeOf<CATALOG_INFO>() };
                    if (!CryptCATCatalogInfoFromContext(cat, ref ci, 0)) continue;
                    string tag = Convert.ToHexString(hash, 0, size);
                    var catPtr = Marshal.StringToHGlobalUni(ci.wszCatalogFile);
                    var tagPtr = Marshal.StringToHGlobalUni(tag);
                    var filePtr = Marshal.StringToHGlobalUni(path);
                    var info = new WINTRUST_CATALOG_INFO
                    {
                        cbStruct = (uint)Marshal.SizeOf<WINTRUST_CATALOG_INFO>(), pcwszCatalogFilePath = catPtr, pcwszMemberTag = tagPtr,
                        pcwszMemberFilePath = filePtr, hCatAdmin = admin,
                    };
                    var infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_CATALOG_INFO>());
                    try
                    {
                        Marshal.StructureToPtr(info, infoPtr, false);
                        int r = Verify(WTD_CHOICE_CATALOG, infoPtr, DriverActionVerify);
                        if (r != 0) r = Verify(WTD_CHOICE_CATALOG, infoPtr, GenericVerifyV2);
                        return (r, ci.wszCatalogFile);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(infoPtr); Marshal.FreeHGlobal(catPtr); Marshal.FreeHGlobal(tagPtr); Marshal.FreeHGlobal(filePtr);
                    }
                }
                finally { CryptCATAdminReleaseCatalogContext(admin, cat, 0); }
            }
            finally { CryptCATAdminReleaseContext(admin, 0); }
        }
        return (TRUST_E_NOSIGNATURE, null);
    }

    private static int Verify(uint choice, IntPtr union, Guid action)
    {
        var data = new WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = WTD_UI_NONE, fdwRevocationChecks = 0,
            dwUnionChoice = choice, pUnion = union, dwStateAction = WTD_STATEACTION_VERIFY,
            dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
        };
        int r = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
        data.dwStateAction = WTD_STATEACTION_CLOSE;
        WinVerifyTrust(new IntPtr(-1), ref action, ref data);
        return r;
    }

    // ---------------------------------------------------------------- Windows

    private const uint WTD_UI_NONE = 2, WTD_CHOICE_FILE = 1, WTD_CHOICE_CATALOG = 2, WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2,
        WTD_REVOCATION_CHECK_NONE = 0x10, WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100), TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003),
        TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private static readonly Guid DriverActionVerify = new("F750E6C3-38EE-11d1-85E5-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO { public uint cbStruct; public IntPtr pcwszFilePath, hFile, pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_CATALOG_INFO
    {
        public uint cbStruct, dwCatalogVersion;
        public IntPtr pcwszCatalogFilePath, pcwszMemberTag, pcwszMemberFilePath, hMemberFile, pbCalculatedFileHash;
        public uint cbCalculatedFileHash;
        public IntPtr pcCatalogContext, hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData, pSIPClientData;
        public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
        public IntPtr pUnion;
        public uint dwStateAction;
        public IntPtr hWVTStateData, pwszURLReference;
        public uint dwProvFlags, dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CATALOG_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string wszCatalogFile;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptCATAdminAcquireContext2(out IntPtr admin, IntPtr subsystem, string hashAlgorithm, IntPtr policy, uint flags);
    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr admin, IntPtr file, ref int size, byte[] hash, uint flags);
    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr admin, byte[] hash, int size, uint flags, ref IntPtr prev);
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptCATCatalogInfoFromContext(IntPtr catInfo, ref CATALOG_INFO info, uint flags);
    [DllImport("wintrust.dll")] private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr admin, IntPtr catInfo, uint flags);
    [DllImport("wintrust.dll")] private static extern bool CryptCATAdminReleaseContext(IntPtr admin, uint flags);
}
