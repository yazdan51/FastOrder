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
            bool supportsOfficialOrderUiAutomation,
            IEnumerable<string>? additionalTrustedOrigins = null,
            IEnumerable<string>? additionalMonitoredHosts = null)
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

            List<string> trustedOrigins =
                BuildTrustedOrigins(
                    trustedOrigin,
                    additionalTrustedOrigins);

            List<string> monitoredHosts =
                BuildMonitoredHosts(
                    monitoredHost,
                    additionalMonitoredHosts);

            string tradingOrigin =
                tradingUri.GetLeftPart(
                    UriPartial.Authority);

            if (!trustedOrigins.Exists(
                origin => string.Equals(
                    origin,
                    tradingOrigin,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    "Broker trading URL origin must be in the trusted-origin allowlist.",
                    nameof(tradingUrl));
            }

            Id =
                id.Trim();

            DisplayName =
                displayName.Trim();

            TradingUrl =
                tradingUri.AbsoluteUri;

            TrustedOrigin =
                trustedOrigins[0];

            TrustedOrigins =
                trustedOrigins.ToArray();

            MonitoredHost =
                monitoredHosts[0];

            MonitoredHosts =
                monitoredHosts.ToArray();

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

        public IReadOnlyList<string> TrustedOrigins
        {
            get;
        }

        public string MonitoredHost
        {
            get;
        }

        public IReadOnlyList<string> MonitoredHosts
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
            if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out Uri? uri))
            {
                return false;
            }

            string pageOrigin =
                uri.GetLeftPart(
                    UriPartial.Authority);

            foreach (string trustedOrigin in TrustedOrigins)
            {
                if (string.Equals(
                    pageOrigin,
                    trustedOrigin,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsMonitoredHost(
            string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            foreach (string monitoredHost in MonitoredHosts)
            {
                if (string.Equals(
                    host,
                    monitoredHost,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> BuildTrustedOrigins(
            string trustedOrigin,
            IEnumerable<string>? additionalTrustedOrigins)
        {
            List<string> origins =
                new List<string>();

            AddTrustedOrigin(
                origins,
                trustedOrigin,
                nameof(trustedOrigin));

            if (additionalTrustedOrigins != null)
            {
                foreach (string additionalOrigin in additionalTrustedOrigins)
                {
                    AddTrustedOrigin(
                        origins,
                        additionalOrigin,
                        nameof(additionalTrustedOrigins));
                }
            }

            return origins;
        }

        private static void AddTrustedOrigin(
            List<string> origins,
            string origin,
            string parameterName)
        {
            if (!Uri.TryCreate(
                origin,
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
                    parameterName);
            }

            string normalizedOrigin =
                originUri.GetLeftPart(
                    UriPartial.Authority);

            if (!origins.Exists(
                value => string.Equals(
                    value,
                    normalizedOrigin,
                    StringComparison.OrdinalIgnoreCase)))
            {
                origins.Add(
                    normalizedOrigin);
            }
        }

        private static List<string> BuildMonitoredHosts(
            string monitoredHost,
            IEnumerable<string>? additionalMonitoredHosts)
        {
            List<string> hosts =
                new List<string>();

            AddMonitoredHost(
                hosts,
                monitoredHost,
                nameof(monitoredHost));

            if (additionalMonitoredHosts != null)
            {
                foreach (string additionalHost in additionalMonitoredHosts)
                {
                    AddMonitoredHost(
                        hosts,
                        additionalHost,
                        nameof(additionalMonitoredHosts));
                }
            }

            return hosts;
        }

        private static void AddMonitoredHost(
            List<string> hosts,
            string host,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(host) ||
                host.IndexOf('/') >= 0 ||
                host.IndexOf(':') >= 0 ||
                !Uri.CheckHostName(host.Trim()).Equals(
                    UriHostNameType.Dns))
            {
                throw new ArgumentException(
                    "Broker monitored host is invalid.",
                    parameterName);
            }

            string normalizedHost =
                host.Trim();

            if (!hosts.Exists(
                value => string.Equals(
                    value,
                    normalizedHost,
                    StringComparison.OrdinalIgnoreCase)))
            {
                hosts.Add(
                    normalizedHost);
            }
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
            "https://kaman.pishrobroker.ir/",
            "https://kaman.pishrobroker.ir",
            "kaman.pishrobroker.ir",
            supportsOfficialOrderUiAutomation: true,
            additionalTrustedOrigins: new[]
            {
                "https://mobile.pishrobroker.ir"
            },
            additionalMonitoredHosts: new[]
            {
                "mobile.pishrobroker.ir"
            });

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
