using System.Runtime.InteropServices;
using System.Text;

namespace Architectus.Services;

/// <summary>
/// Stores API keys in Windows Credential Manager (generic credentials, scoped to the
/// current Windows user). Windows encrypts these with DPAPI. Keys are never written to
/// any Architectus file, log, or settings JSON.
///
/// You can see them yourself: Control Panel → Credential Manager → Windows Credentials,
/// entries named "Architectus:openai", "Architectus:anthropic", "Architectus:gemini".
/// </summary>
public static class CredentialStore
{
    private const string Prefix = "Architectus:";
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2; // survives sign-out, still per-user
    private const int ERROR_NOT_FOUND = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static void Save(string providerId, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Key is empty.");
        byte[] blob = Encoding.Unicode.GetBytes(secret.Trim());
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = Prefix + providerId,
                Comment = "Architectus API key",
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref cred, 0))
                throw new InvalidOperationException($"Windows refused to save the key (error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            // Wipe our unmanaged copy before freeing it.
            for (int i = 0; i < blob.Length; i++) Marshal.WriteByte(blobPtr, i, 0);
            Marshal.FreeHGlobal(blobPtr);
            Array.Clear(blob);
        }
    }

    public static string? Load(string providerId)
    {
        if (!CredRead(Prefix + providerId, CRED_TYPE_GENERIC, 0, out IntPtr ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public static bool Exists(string providerId) => Load(providerId) is not null;

    public static void Delete(string providerId)
    {
        if (!CredDelete(Prefix + providerId, CRED_TYPE_GENERIC, 0))
        {
            int err = Marshal.GetLastWin32Error();
            if (err != ERROR_NOT_FOUND)
                throw new InvalidOperationException($"Windows could not remove the key (error {err}).");
        }
    }

    /// <summary>Removes every Architectus key. Used by "Remove all keys" and by the uninstaller.</summary>
    public static void DeleteAll()
    {
        foreach (var p in ProviderCatalog.All)
        {
            try { Delete(p.Id); } catch { /* best effort */ }
        }
    }
}
