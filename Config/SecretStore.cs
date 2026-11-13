using System.Security.Cryptography;
using System.Text;

namespace SysWatch.Config;

// DPAPI (CurrentUser): the blob only decrypts for this Windows user on this machine.
public static class SecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SysWatch.v1");

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    public static string Unprotect(string? blob)
    {
        if (string.IsNullOrEmpty(blob)) return "";
        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(blob), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            Log.Warn(L.T("Şifreli bir ayar çözülemedi (başka kullanıcı/bilgisayardan kopyalanmış olabilir), boş bırakıldı.", "An encrypted setting couldn't be decrypted (it may have been copied from another user or PC) and was left empty."));
            return "";
        }
    }
}
