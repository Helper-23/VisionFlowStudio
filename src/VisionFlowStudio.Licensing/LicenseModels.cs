using System;
using System.Runtime.Serialization;

namespace VisionFlowStudio.Licensing
{
    [DataContract]
    public sealed class LicensePayload
    {
        [DataMember(Order = 1)] public int FormatVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string ProductId { get; set; } = LicenseConstants.ProductId;
        [DataMember(Order = 3)] public string LicenseId { get; set; } = Guid.NewGuid().ToString("N").ToUpperInvariant();
        [DataMember(Order = 4)] public string MachineCode { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string Customer { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string Edition { get; set; } = "Professional";
        [DataMember(Order = 7)] public string[] Features { get; set; } = new string[0];
        [DataMember(Order = 8)] public long IssuedUtcTicks { get; set; }
        [DataMember(Order = 9)] public long ExpiresUtcTicks { get; set; }

        public DateTime IssuedUtc => new DateTime(IssuedUtcTicks, DateTimeKind.Utc);
        public DateTime? ExpiresUtc => ExpiresUtcTicks <= 0
            ? (DateTime?)null
            : new DateTime(ExpiresUtcTicks, DateTimeKind.Utc);
    }

    public enum LicenseErrorCode
    {
        None,
        Missing,
        InvalidFormat,
        InvalidSignature,
        WrongProduct,
        WrongMachine,
        NotYetValid,
        Expired,
        ClockRollback,
        StorageError
    }

    public sealed class LicenseValidationResult
    {
        public bool IsValid { get; internal set; }
        public LicenseErrorCode ErrorCode { get; internal set; }
        public string Message { get; internal set; }
        public LicensePayload License { get; internal set; }

        internal static LicenseValidationResult Success(LicensePayload license)
        {
            return new LicenseValidationResult
            {
                IsValid = true,
                ErrorCode = LicenseErrorCode.None,
                Message = "License is valid.",
                License = license
            };
        }

        internal static LicenseValidationResult Failure(LicenseErrorCode code, string message, LicensePayload license = null)
        {
            return new LicenseValidationResult
            {
                IsValid = false,
                ErrorCode = code,
                Message = message,
                License = license
            };
        }
    }
}
