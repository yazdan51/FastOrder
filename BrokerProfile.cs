using System;
using System.Collections.Generic;

namespace FastOrder
{
    internal sealed class BrokerProfile
    {
        public BrokerProfile(
            string id,
            string displayName,
            string tradingUrl,
            string trustedOrigin,
            string monitoredHost,
            bool supportsOfficialOrderUiAutomation)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "Broker id cannot be empty.",
                    nameof(id));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "Broker display name cannot be empty.",
                    nameof(displayName));
            }

            if (!Uri.TryCreate(
                tradingUrl,
                UriKind.Absolute,
                out Uri? tradingUri) ||
                !tradingUri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Broker trading URL must be an absolute HTTPS URL.",
                    nameof(tradingUrl));
            }

            if (!Uri.TryCreate(
                trustedOrigin,
                UriKind.Absolute,
                out Uri? originUri) ||
                !originUri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                originUri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(originUri.Query) ||
                !string.IsNullOrEmpty(originUri.Fragment))
            {
                throw new ArgumentException(
                    "Broker trusted origin must be an HTTPS origin without path, query, or fragment.",
                    nameof(trustedOrigin));
            }

            if (string.IsNullOrWhiteSpace(monitoredHost) ||
                monitoredHost.IndexOf('/') >= 0)
            {
                throw new ArgumentException(
                    "Broker monitored host is invalid.",
                    nameof(monitoredHost));
            }

            Id =
                id.Trim();

            DisplayName =
                displayName.Trim();

            TradingUrl =
                tradingUri.AbsoluteUri;

            TrustedOrigin =
                originUri.GetLeftPart(
                    UriPartial.Authority);

            MonitoredHost =
                monitoredHost.Trim();

            SupportsOfficialOrderUiAutomation =
                supportsOfficialOrderUiAutomation;
        }

        public string Id
        {
            get;
        }

        public string DisplayName
        {
            get;
        }

        public string TradingUrl
        {
            get;
        }

        public string TrustedOrigin
        {
            get;
        }

        public string MonitoredHost
        {
            get;
        }

        public bool SupportsOfficialOrderUiAutomation
        {
            get;
        }

        public bool IsTrustedPage(
            string? url)
        {
            return
                Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out Uri? uri) &&
                string.Equals(
                    uri.GetLeftPart(
                        UriPartial.Authority),
                    TrustedOrigin,
                    StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() =>
            DisplayName;
    }

    internal static class BrokerProfiles
    {
        public const string EasyTraderId =
            "easytrader";

        public const string PishroKamanId =
            "pishro-kaman";

        public static BrokerProfile EasyTrader
        {
            get;
        } = new BrokerProfile(
            EasyTraderId,
            "EasyTrader",
            "https://d.easytrader.ir/",
            "https://d.easytrader.ir",
            "api-mts.orbis.easytrader.ir",
            supportsOfficialOrderUiAutomation: true);

        public static BrokerProfile PishroKaman
        {
            get;
        } = new BrokerProfile(
            PishroKamanId,
            "پیشرو — کمان",
            "https://kaman.pishrobroker.ir/trading-view/IRO9MSMI0D81",
            "https://kaman.pishrobroker.ir",
            "kaman.pishrobroker.ir",
            supportsOfficialOrderUiAutomation: false);

        public static IReadOnlyList<BrokerProfile> All
        {
            get;
        } = new[]
        {
            EasyTrader,
            PishroKaman
        };

        public static BrokerProfile ResolveOrDefault(
            string? id)
        {
            foreach (BrokerProfile profile in All)
            {
                if (string.Equals(
                    profile.Id,
                    id,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return EasyTrader;
        }

        public static string GetDisplayName(
            string id)
        {
            foreach (BrokerProfile profile in All)
            {
                if (string.Equals(
                    profile.Id,
                    id,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return profile.DisplayName;
                }
            }

            return id;
        }
    }
}
