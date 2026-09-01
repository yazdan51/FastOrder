using System;

namespace FastOrder
{
    /// <summary>
    /// مسیر انتخاب Adapter رابط رسمی سفارش را در یک نقطه نگه می‌دارد تا
    /// selectorها و اسکریپت‌های دو کارگزاری هرگز با یکدیگر ترکیب نشوند.
    /// </summary>
    internal static class BrokerOfficialOrderUiBridge
    {
        public static string BuildOpenCurrentSymbolBuyDialogScript(
            BrokerProfile broker) =>
            IsPishroKaman(broker)
                ? PishroKamanOrderUiBridge.BuildOpenCurrentSymbolBuyDialogScript()
                : OfficialOrderUiBridge.BuildOpenCurrentSymbolBuyDialogScript();

        public static string BuildReadCurrentOrderFormScript(
            BrokerProfile broker) =>
            IsPishroKaman(broker)
                ? PishroKamanOrderUiBridge.BuildReadCurrentOrderFormScript()
                : OfficialOrderUiBridge.BuildReadCurrentOrderFormScript();

        public static string BuildEnsureBuyDialogScript(
            BrokerProfile broker,
            Order order) =>
            IsPishroKaman(broker)
                ? PishroKamanOrderUiBridge.BuildEnsureBuyDialogScript(order)
                : OfficialOrderUiBridge.BuildEnsureBuyDialogScript(order);

        public static string BuildPrepareScript(
            BrokerProfile broker,
            Order order,
            string nonce) =>
            IsPishroKaman(broker)
                ? PishroKamanOrderUiBridge.BuildPrepareScript(order, nonce)
                : OfficialOrderUiBridge.BuildPrepareScript(order, nonce);

        public static string BuildSubmitScript(
            BrokerProfile broker,
            Order order,
            string nonce) =>
            IsPishroKaman(broker)
                ? PishroKamanOrderUiBridge.BuildSubmitScript(order, nonce)
                : OfficialOrderUiBridge.BuildSubmitScript(order, nonce);

        public static string BuildAtomicScheduledSubmitScript(
            BrokerProfile broker,
            Order order,
            string nonce) =>
            IsPishroKaman(broker)
                ? PishroKamanOrderUiBridge.BuildAtomicScheduledSubmitScript(order, nonce)
                : OfficialOrderUiBridge.BuildAtomicScheduledSubmitScript(order, nonce);

        public static string BuildClearScript(
            BrokerProfile broker,
            string nonce) =>
            IsPishroKaman(broker)
                ? PishroKamanOrderUiBridge.BuildClearScript(nonce)
                : OfficialOrderUiBridge.BuildClearScript(nonce);

        private static bool IsPishroKaman(
            BrokerProfile broker)
        {
            ArgumentNullException.ThrowIfNull(broker);

            return string.Equals(
                broker.Id,
                BrokerProfiles.PishroKamanId,
                StringComparison.Ordinal);
        }
    }
}
