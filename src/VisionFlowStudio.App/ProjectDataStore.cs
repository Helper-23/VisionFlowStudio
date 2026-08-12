using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.App
{
    public static class ProjectDataStore
    {
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("VFSENC01");
        private const int SaltLength = 16;
        private const int IvLength = 16;
        private const int MacLength = 32;
        private const int Iterations = 120000;

        public static ProjectDocument Load(string path, string password)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("方案路径不能为空。", nameof(path));
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("请输入方案密码。", nameof(password));
            var payload = File.ReadAllBytes(path);
            var json = Decrypt(payload, password);
            var serializer = new DataContractJsonSerializer(typeof(ProjectDocument));
            using (var stream = new MemoryStream(json, false)) return (ProjectDocument)serializer.ReadObject(stream);
        }

        public static void Save(ProjectDocument project, string path, string password)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("方案路径不能为空。", nameof(path));
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("必须为视觉方案设置密码。", nameof(password));
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            byte[] json;
            var serializer = new DataContractJsonSerializer(typeof(ProjectDocument));
            using (var stream = new MemoryStream()) { serializer.WriteObject(stream, project); json = stream.ToArray(); }
            var encrypted = Encrypt(json, password);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static bool IsEncryptedProject(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length < Header.Length) return false;
                for (var i = 0; i < Header.Length; i++) if (stream.ReadByte() != Header[i]) return false;
                return true;
            }
        }

        private static byte[] Encrypt(byte[] plain, string password)
        {
            var salt = RandomBytes(SaltLength);
            var iv = RandomBytes(IvLength);
            byte[] encryptionKey, authenticationKey;
            DeriveKeys(password, salt, out encryptionKey, out authenticationKey);
            byte[] cipher;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256; aes.BlockSize = 128; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey; aes.IV = iv;
                using (var output = new MemoryStream())
                using (var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write))
                { crypto.Write(plain, 0, plain.Length); crypto.FlushFinalBlock(); cipher = output.ToArray(); }
            }
            using (var body = new MemoryStream())
            using (var writer = new BinaryWriter(body, Encoding.UTF8, true))
            {
                writer.Write(Header); writer.Write(salt); writer.Write(iv); writer.Write(cipher.Length); writer.Write(cipher); writer.Flush();
                byte[] mac;
                using (var hmac = new HMACSHA256(authenticationKey)) mac = hmac.ComputeHash(body.ToArray());
                writer.Write(mac); writer.Flush();
                Clear(encryptionKey); Clear(authenticationKey);
                return body.ToArray();
            }
        }

        private static byte[] Decrypt(byte[] payload, string password)
        {
            try
            {
                if (payload == null || payload.Length < Header.Length + SaltLength + IvLength + sizeof(int) + MacLength) throw new CryptographicException();
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    var header = reader.ReadBytes(Header.Length);
                    if (!FixedEquals(header, Header)) throw new InvalidDataException("该文件不是 VisionFlow Studio 加密方案。");
                    var salt = reader.ReadBytes(SaltLength);
                    var iv = reader.ReadBytes(IvLength);
                    var cipherLength = reader.ReadInt32();
                    if (cipherLength <= 0 || cipherLength > payload.Length - stream.Position - MacLength) throw new CryptographicException();
                    var cipher = reader.ReadBytes(cipherLength);
                    var storedMac = reader.ReadBytes(MacLength);
                    byte[] encryptionKey, authenticationKey;
                    DeriveKeys(password, salt, out encryptionKey, out authenticationKey);
                    var authenticatedLength = payload.Length - MacLength;
                    byte[] calculatedMac;
                    using (var hmac = new HMACSHA256(authenticationKey)) calculatedMac = hmac.ComputeHash(payload, 0, authenticatedLength);
                    if (!FixedEquals(storedMac, calculatedMac)) throw new CryptographicException();
                    using (var aes = Aes.Create())
                    {
                        aes.KeySize = 256; aes.BlockSize = 128; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                        aes.Key = encryptionKey; aes.IV = iv;
                        using (var input = new MemoryStream(cipher, false))
                        using (var crypto = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read))
                        using (var output = new MemoryStream())
                        { crypto.CopyTo(output); Clear(encryptionKey); Clear(authenticationKey); return output.ToArray(); }
                    }
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) when (ex is CryptographicException || ex is EndOfStreamException || ex is ArgumentException)
            { throw new CryptographicException("方案密码错误或文件已损坏。", ex); }
        }

        private static void DeriveKeys(string password, byte[] salt, out byte[] encryptionKey, out byte[] authenticationKey)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                var material = derive.GetBytes(64);
                encryptionKey = new byte[32]; authenticationKey = new byte[32];
                Buffer.BlockCopy(material, 0, encryptionKey, 0, 32); Buffer.BlockCopy(material, 32, authenticationKey, 0, 32);
                Clear(material);
            }
        }

        private static byte[] RandomBytes(int length) { var value = new byte[length]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(value); return value; }
        private static bool FixedEquals(byte[] left, byte[] right) { if (left == null || right == null || left.Length != right.Length) return false; var difference = 0; for (var i = 0; i < left.Length; i++) difference |= left[i] ^ right[i]; return difference == 0; }
        private static void Clear(byte[] value) { if (value != null) Array.Clear(value, 0, value.Length); }
    }
}
