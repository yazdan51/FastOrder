using System;
using System.Security.Cryptography;
using System.Text;

namespace FastOrder
{
    public sealed class ConfirmedOrderSnapshot
    {
        private ConfirmedOrderSnapshot(
            string payloadJson,
            string fingerprint,
            DateTimeOffset confirmedAtUtc)
        {
            PayloadJson =
                payloadJson;

            Fingerprint =
                fingerprint;

            ConfirmedAtUtc =
                confirmedAtUtc;
        }

        public string PayloadJson
        {
            get;
        }

        public string Fingerprint
        {
            get;
        }

        public DateTimeOffset ConfirmedAtUtc
        {
            get;
        }

        public string ShortFingerprint =>
            Fingerprint[..16];

        public static ConfirmedOrderSnapshot Create(
            string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                throw new ArgumentException(
                    "Confirmed payload JSON cannot be empty.",
                    nameof(payloadJson));
            }

            string fingerprint =
                ComputeFingerprint(
                    payloadJson);

            return new ConfirmedOrderSnapshot(
                payloadJson,
                fingerprint,
                DateTimeOffset.UtcNow);
        }

        public bool HasValidFingerprint()
        {
            string currentFingerprint =
                ComputeFingerprint(
                    PayloadJson);

            return string.Equals(
                Fingerprint,
                currentFingerprint,
                StringComparison.Ordinal);
        }

        private static string ComputeFingerprint(
            string payloadJson)
        {
            byte[] payloadBytes =
                Encoding.UTF8.GetBytes(
                    payloadJson);

            byte[] hash =
                SHA256.HashData(
                    payloadBytes);

            return Convert.ToHexString(
                hash);
        }
    }
}
