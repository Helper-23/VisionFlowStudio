using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace VisionFlowStudio.Licensing
{
    public static class LicenseConstants
    {
        public const string ProductId = "VisionFlowStudio";
        public const string LicensePrefix = "VFS1";

        // Only the public key is shipped with the application. The private key belongs to the
        // offline license issuer and must never be copied to a customer computer.
        public const string PublicKeyXml = "<RSAKeyValue><Modulus>4Sa5QEnJt/MTfnKsNHJuxGL5B+KG8fSjk68/1wl6NbtPqB/0k8xpEkhY1wSQhF4oHGsAzP8vVM8TRh3jn7oBoLGMnz8qsJbI94GJ1AKTtbtZTZu7ZQcJdk6YlG4jp9rOdda6nDR9RNt5OPm8g9rms5rtGwBAesHUxFBY9skxM9CctmOHuWRYykaeljyM6u3ug1XC0Z2qIiRY1tYr5zfrAWDLxnt5cbzZ8JIc0kNXBLVSlJ2tbImnfA6yXo33KBYCpUIvHbm+pA6D2mc/MpI9/a3l+5TV3pWFf5CzbrY+OQlwMQrhLJqpLqEOql211BEkcNeIXciUZQ9b9OC28HW71yxIRH2wpO9E+LPqKIo/sHdHS14+1JufPS1J0yN/Fb7bz2t5RmtHf2NRFth5Zh/RY3Cax5SExCzvKqarP5UKj6pJPp3EBeHbkJ4cvYSF1paIhTFtGDTEPsvgin1Zj2JZYlmJ5wyzdAG+n9mzKoUu9Mc/vv9/wzXYOAakXOadhiPJ</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    }

    public static class LicenseCodec
    {
        public static string CreateLicense(LicensePayload payload, string privateKeyXml)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (string.IsNullOrWhiteSpace(privateKeyXml)) throw new ArgumentException("Private key is required.", nameof(privateKeyXml));

            var json = Serialize(payload);
            var data = Encoding.UTF8.GetBytes(json);
            byte[] signature;
            using (var rsa = CreateRsa())
            {
                rsa.FromXmlString(privateKeyXml);
                signature = rsa.SignData(data, CryptoConfig.MapNameToOID("SHA256"));
            }
            return LicenseConstants.LicensePrefix + "." + ToBase64Url(data) + "." + ToBase64Url(signature);
        }

        public static LicenseValidationResult Validate(string licenseKey, string machineCode = null,
            DateTime? utcNow = null, string publicKeyXml = null)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return LicenseValidationResult.Failure(LicenseErrorCode.Missing, "No license is installed.");

            try
            {
                var normalized = RemoveWhitespace(licenseKey);
                var parts = normalized.Split('.');
                if (parts.Length != 3 || !string.Equals(parts[0], LicenseConstants.LicensePrefix, StringComparison.Ordinal))
                    return LicenseValidationResult.Failure(LicenseErrorCode.InvalidFormat, "The license format is invalid.");

                var data = FromBase64Url(parts[1]);
                var signature = FromBase64Url(parts[2]);
                using (var rsa = CreateRsa())
                {
                    rsa.FromXmlString(publicKeyXml ?? LicenseConstants.PublicKeyXml);
                    if (!rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256"), signature))
                        return LicenseValidationResult.Failure(LicenseErrorCode.InvalidSignature, "The license signature is invalid.");
                }

                var payload = Deserialize(Encoding.UTF8.GetString(data));
                if (payload == null || payload.FormatVersion != 1)
                    return LicenseValidationResult.Failure(LicenseErrorCode.InvalidFormat, "The license version is not supported.");
                if (!string.Equals(payload.ProductId, LicenseConstants.ProductId, StringComparison.Ordinal))
                    return LicenseValidationResult.Failure(LicenseErrorCode.WrongProduct, "The license belongs to another product.", payload);

                var expectedMachine = MachineFingerprint.NormalizeMachineCode(machineCode ?? MachineFingerprint.GetMachineCode());
                var licensedMachine = MachineFingerprint.NormalizeMachineCode(payload.MachineCode);
                if (!string.Equals(expectedMachine, licensedMachine, StringComparison.Ordinal))
                    return LicenseValidationResult.Failure(LicenseErrorCode.WrongMachine, "The license does not belong to this computer.", payload);

                var now = utcNow ?? DateTime.UtcNow;
                if (payload.IssuedUtcTicks > 0 && payload.IssuedUtc > now.AddMinutes(10))
                    return LicenseValidationResult.Failure(LicenseErrorCode.NotYetValid, "The license is not valid yet. Check the system time.", payload);
                if (payload.ExpiresUtc.HasValue && now > payload.ExpiresUtc.Value)
                    return LicenseValidationResult.Failure(LicenseErrorCode.Expired, "The license has expired.", payload);
                return LicenseValidationResult.Success(payload);
            }
            catch (FormatException exception)
            {
                return LicenseValidationResult.Failure(LicenseErrorCode.InvalidFormat, exception.Message);
            }
            catch (CryptographicException exception)
            {
                return LicenseValidationResult.Failure(LicenseErrorCode.InvalidSignature, exception.Message);
            }
            catch (Exception exception)
            {
                return LicenseValidationResult.Failure(LicenseErrorCode.InvalidFormat, exception.Message);
            }
        }

        private static RSACryptoServiceProvider CreateRsa()
        {
            var parameters = new CspParameters { ProviderType = 24 };
            return new RSACryptoServiceProvider(parameters) { PersistKeyInCsp = false };
        }

        private static string Serialize(LicensePayload payload)
        {
            var serializer = new DataContractJsonSerializer(typeof(LicensePayload));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, payload);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static LicensePayload Deserialize(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(LicensePayload));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return serializer.ReadObject(stream) as LicensePayload;
        }

        private static string ToBase64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromBase64Url(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }

        private static string RemoveWhitespace(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
                if (!char.IsWhiteSpace(character)) builder.Append(character);
            return builder.ToString();
        }
    }
}
