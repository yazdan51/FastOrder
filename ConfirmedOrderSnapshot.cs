using System;
using System.Security.Cryptography;
using System.Text;

namespace FastOrder
{
    public sealed class ConfirmedOrderSnapshot
    {
        private ConfirmedOrderSnapshot(
            string brokerId,
            string payloadJson,
            string fingerprint,
            DateTimeOffset confirmedAtUtc)
        {
            BrokerId =
                brokerId;

            PayloadJson =
                payloadJson;

            Fingerprint =
                fingerprint;

            ConfirmedAtUtc =
                confirmedAtUtc;
        }

        public string BrokerId
        {
            get;
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
            string brokerId,
            string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(brokerId))
            {
                throw new ArgumentException(
                    "Confirmed broker id cannot be empty.",
                    nameof(brokerId));
            }

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                throw new ArgumentException(
                    "Confirmed payload JSON cannot be empty.",
                    nameof(payloadJson));
            }

            string fingerprint =
                ComputeFingerprint(
                    brokerId,
                    payloadJson);

            return new ConfirmedOrderSnapshot(
                brokerId,
                payloadJson,
                fingerprint,
                DateTimeOffset.UtcNow);
        }

        public bool HasValidFingerprint()
        {
            string currentFingerprint =
                ComputeFingerprint(
                    BrokerId,
                    PayloadJson);

            return string.Equals(
                Fingerprint,
                currentFingerprint,
                StringComparison.Ordinal);
        }

        public ConfirmedOrderSnapshot CreateIndependentCopy()
        {
            if (!HasValidFingerprint())
            {
                throw new InvalidOperationException(
                    "Cannot copy a confirmed order snapshot with an invalid fingerprint.");
            }

            return new ConfirmedOrderSnapshot(
                BrokerId,
                PayloadJson,
                Fingerprint,
                ConfirmedAtUtc);
        }

        private static string ComputeFingerprint(
            string brokerId,
            string payloadJson)
        {
            byte[] snapshotBytes =
                Encoding.UTF8.GetBytes(
                    brokerId +
                    "\n" +
                    payloadJson);

            byte[] hash =
                SHA256.HashData(
                    snapshotBytes);

            return Convert.ToHexString(
                hash);
        }
    }
}
