using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace VisionFlowStudio.Licensing
{
    public static class MachineFingerprint
    {
        public static string GetMachineCode()
        {
            var machineGuid = ReadMachineGuid();
            var volumeSerial = ReadSystemVolumeSerial();
            var material = "VFS-MACHINE-V1|" + machineGuid + "|" + volumeSerial;

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                var text = BitConverter.ToString(hash, 0, 20).Replace("-", string.Empty);
                var builder = new StringBuilder("VFS");
                for (var i = 0; i < text.Length; i += 5)
                    builder.Append('-').Append(text.Substring(i, Math.Min(5, text.Length - i)));
                return builder.ToString();
            }
        }

        public static string NormalizeMachineCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var compact = new StringBuilder();
            foreach (var character in value.ToUpperInvariant())
                if (char.IsLetterOrDigit(character)) compact.Append(character);

            var text = compact.ToString();
            if (text.StartsWith("VFS", StringComparison.Ordinal)) text = text.Substring(3);
            var builder = new StringBuilder("VFS");
            for (var i = 0; i < text.Length; i += 5)
                builder.Append('-').Append(text.Substring(i, Math.Min(5, text.Length - i)));
            return builder.ToString();
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    var value = key?.GetValue("MachineGuid") as string;
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim().ToUpperInvariant();
                }
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    var value = key?.GetValue("MachineGuid") as string;
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim().ToUpperInvariant();
                }
            }
            catch { }
            return Environment.MachineName.ToUpperInvariant();
        }

        private static string ReadSystemVolumeSerial()
        {
            try
            {
                var root = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
                uint serial;
                uint maxComponentLength;
                uint fileSystemFlags;
                var volumeName = new StringBuilder(261);
                var fileSystemName = new StringBuilder(261);
                if (GetVolumeInformation(root, volumeName, volumeName.Capacity, out serial,
                    out maxComponentLength, out fileSystemFlags, fileSystemName, fileSystemName.Capacity))
                    return serial.ToString("X8");
            }
            catch { }
            return "NO-VOLUME";
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetVolumeInformation(
            string rootPathName,
            StringBuilder volumeNameBuffer,
            int volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            StringBuilder fileSystemNameBuffer,
            int fileSystemNameSize);
    }
}
