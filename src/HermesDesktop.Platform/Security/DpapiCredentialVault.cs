using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HermesDesktop.Core.Auth;
using HermesDesktop.Core.Storage;

namespace HermesDesktop.Platform.Security;

public interface ICredentialVault
{
    DashboardSecrets? LoadSecrets(Guid dashboardId);
    void SaveSecrets(Guid dashboardId, DashboardSecrets secrets);
    void DeleteSecrets(Guid dashboardId);
}

/// <summary>
/// Persists dashboard credentials in vault.dat:
/// - On Windows: encrypted with Windows DPAPI (CurrentUser scope).
/// - On macOS/Linux (dev preview): encrypted with AES-256-GCM derived from local user/machine entropy.
/// </summary>
public sealed class DpapiCredentialVault : ICredentialVault
{
    private readonly string _vaultPath;
    private readonly object _lock = new();

    public DpapiCredentialVault(string? vaultPath = null)
    {
        _vaultPath = vaultPath ?? AppDataPaths.VaultFilePath;
    }

    public DashboardSecrets? LoadSecrets(Guid dashboardId)
    {
        lock (_lock)
        {
            var map = ReadAll();
            return map.TryGetValue(dashboardId, out var s) ? s : null;
        }
    }

    public void SaveSecrets(Guid dashboardId, DashboardSecrets secrets)
    {
        lock (_lock)
        {
            var map = ReadAll();
            map[dashboardId] = secrets;
            WriteAll(map);
        }
    }

    public void DeleteSecrets(Guid dashboardId)
    {
        lock (_lock)
        {
            var map = ReadAll();
            if (map.Remove(dashboardId))
            {
                WriteAll(map);
            }
        }
    }

    private Dictionary<Guid, DashboardSecrets> ReadAll()
    {
        try
        {
            if (!File.Exists(_vaultPath)) return [];
            var raw = File.ReadAllBytes(_vaultPath);
            if (raw.Length == 0) return [];

            byte[] plain;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    plain = ProtectedData.Unprotect(raw, EntropyBytes, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    plain = ProtectedData.Unprotect(raw, LegacyEntropyBytes, DataProtectionScope.CurrentUser);
                }
            }
            else
            {
                try
                {
                    plain = UnprotectDev(raw, DevKey());
                }
                catch
                {
                    plain = UnprotectDev(raw, LegacyDevKey());
                }
            }

            return JsonSerializer.Deserialize<Dictionary<Guid, DashboardSecrets>>(plain) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteAll(Dictionary<Guid, DashboardSecrets> map)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_vaultPath)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(map);
        byte[] protectedBytes;
        if (OperatingSystem.IsWindows())
        {
            protectedBytes = ProtectedData.Protect(plain, EntropyBytes, DataProtectionScope.CurrentUser);
        }
        else
        {
            protectedBytes = ProtectDev(plain);
        }
        File.WriteAllBytes(_vaultPath, protectedBytes);
    }

    private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("HermesDesktop.Windows.Vault.v1");
    private static readonly byte[] LegacyEntropyBytes = Encoding.UTF8.GetBytes("HermesConduit.Windows.Vault.v1");

    private static byte[] DevKey()
    {
        var seed = $"{Environment.UserName}|{Environment.MachineName}|HermesDesktop.DevVault.v1";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    private static byte[] LegacyDevKey()
    {
        var seed = $"{Environment.UserName}|{Environment.MachineName}|HermesConduit.DevVault.v1";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    private static byte[] ProtectDev(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(DevKey(), 16);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, output, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, output, nonce.Length + tag.Length, cipher.Length);
        return output;
    }

    private static byte[] UnprotectDev(byte[] blob, byte[] key)
    {
        if (blob.Length < 28) throw new CryptographicException("Invalid vault payload.");
        var nonce = blob.AsSpan(0, 12);
        var tag = blob.AsSpan(12, 16);
        var cipher = blob.AsSpan(28);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
