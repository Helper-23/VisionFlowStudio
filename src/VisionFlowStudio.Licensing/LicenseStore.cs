using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VisionFlowStudio.Licensing
{
    public static class LicenseStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VisionFlowStudio.LicenseStore.v1");
        private static readonly object SyncRoot = new object();

        public static string LicenseDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VisionFlowStudio", "License");

        public static string LicensePath => Path.Combine(LicenseDirectory, "license.dat");
        private static string RuntimePath => Path.Combine(LicenseDirectory, "runtime.dat");

        public static LicenseValidationResult ValidateInstalled()
        {
            lock (SyncRoot)
            {
                try
                {
                    var key = Environment.GetEnvironmentVariable("VFS_LICENSE_KEY");
                    if (string.IsNullOrWhiteSpace(key)) key = ReadProtectedText(LicensePath);
                    var result = LicenseCodec.Validate(key);
                    if (!result.IsValid) return result;

                    var now = DateTime.UtcNow;
                    var lastUtc = ReadLastRunUtc();
                    if (lastUtc.HasValue && now < lastUtc.Value.AddMinutes(-10))
                        return LicenseValidationResult.Failure(LicenseErrorCode.ClockRollback,
                            "The system clock was moved backwards. Correct the time and contact the supplier if necessary.", result.License);

                    WriteLastRunUtc(lastUtc.HasValue && lastUtc.Value > now ? lastUtc.Value : now);
                    return result;
                }
                catch (Exception exception)
                {
                    return LicenseValidationResult.Failure(LicenseErrorCode.StorageError, exception.Message);
                }
            }
        }

        public static LicenseValidationResult Install(string licenseKey)
        {
            lock (SyncRoot)
            {
                var result = LicenseCodec.Validate(licenseKey);
                if (!result.IsValid) return result;
                try
                {
                    Directory.CreateDirectory(LicenseDirectory);
                    WriteProtectedText(LicensePath, RemoveWhitespace(licenseKey));
                    WriteLastRunUtc(DateTime.UtcNow);
                    return result;
                }
                catch (Exception exception)
                {
                    return LicenseValidationResult.Failure(LicenseErrorCode.StorageError, exception.Message, result.License);
                }
            }
        }

        public static void RemoveInstalled()
        {
            lock (SyncRoot)
            {
                if (File.Exists(LicensePath)) File.Delete(LicensePath);
                if (File.Exists(RuntimePath)) File.Delete(RuntimePath);
            }
        }

        private static DateTime? ReadLastRunUtc()
        {
            var text = ReadProtectedText(RuntimePath);
            long ticks;
            if (long.TryParse(text, out ticks) && ticks > 0)
                return new DateTime(ticks, DateTimeKind.Utc);
            return null;
        }

        private static void WriteLastRunUtc(DateTime utc)
        {
            Directory.CreateDirectory(LicenseDirectory);
            WriteProtectedText(RuntimePath, utc.ToUniversalTime().Ticks.ToString());
        }

        private static string ReadProtectedText(string path)
        {
            if (!File.Exists(path)) return null;
            var encrypted = File.ReadAllBytes(path);
            var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(clear);
        }

        private static void WriteProtectedText(string path, string value)
        {
            var clear = Encoding.UTF8.GetBytes(value ?? string.Empty);
            var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.LocalMachine);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            if (File.Exists(path))
            {
                var backup = path + ".bak";
                File.Replace(temporary, path, backup, true);
                if (File.Exists(backup)) File.Delete(backup);
            }
            else File.Move(temporary, path);
        }

        private static string RemoveWhitespace(string value)
        {
            if (value == null) return null;
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
                if (!char.IsWhiteSpace(character)) builder.Append(character);
            return builder.ToString();
        }
    }
}
