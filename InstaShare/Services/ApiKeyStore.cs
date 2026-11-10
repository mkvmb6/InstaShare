using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace InstaShare.Services
{
    /// <summary>
    /// The InstaShare API key, kept beside settings.json but encrypted with Windows' per-user data protection,
    /// so another account on the PC, or a copy of the file on another PC, can't read it.
    /// </summary>
    internal sealed class ApiKeyStore(string path)
    {
        // Ties the encryption to this app as well as to the Windows account.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("InstaShare API key v1");

        private string? _cached;
        private bool _loaded;

        /// <summary>
        /// The key, or null if none is saved or the file can't be read.
        /// </summary>
        public string? Get()
        {
            if (_loaded)
                return _cached;
            _loaded = true;
            try
            {
                if (File.Exists(path))
                    _cached = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception)
            {
                // Copied from another account or PC, or damaged: treat as missing so the person is asked again.
                _cached = null;
            }
            return _cached;
        }

        /// <summary>
        /// Saves the key, or removes it when <paramref name="key"/> is empty.
        /// </summary>
        public void Set(string? key)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                File.Delete(path);
                key = null;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllBytes(temp, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));
                File.Move(temp, path, overwrite: true);
            }
            _cached = key;
            _loaded = true;
        }

        /// <summary>
        /// A quick format check before sending; the server decides whether the key is real.
        /// </summary>
        public static bool LooksValid(string? key) =>
            key is { Length: > 20 and < 100 } && key.StartsWith("isk_", StringComparison.Ordinal) && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

        /// <summary>
        /// "isk_…a1b2", for showing which key is saved without showing the key.
        /// </summary>
        public static string Mask(string key) => key.Length <= 8 ? "isk_…" : $"isk_…{key[^4..]}";
    }
}
