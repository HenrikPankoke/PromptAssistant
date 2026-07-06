using System.Security.Cryptography;
using System.Text;

namespace PromptAssistant.Services
{
    internal static class EncryptionService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PromptAssistant-v1");

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        /// <summary>
        /// Decrypts a DPAPI-protected value. Falls back to returning the value as-is
        /// when it is plain text (migration from an unencrypted settings file).
        /// </summary>
        public static string Unprotect(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            try
            {
                var bytes = Convert.FromBase64String(cipherText);
                var decrypted = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return cipherText; // plain text — will be encrypted on the next Save()
            }
        }
    }
}
