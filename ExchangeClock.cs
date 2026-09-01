using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FastOrder
{
    internal readonly record struct ExchangeClockReading(
        DateTimeOffset Now,
        TimeSpan SampleAge,
        TimeSpan RoundTripTime,
        TimeSpan EstimatedUncertainty);

    /// <summary>
    /// Maintains a fail-closed clock synchronized from the public HTTPS
    /// TSETMC market server. Wall-clock progression after a successful sample
    /// uses Stopwatch, so later Windows clock adjustments cannot move a
    /// scheduled slot forward or backward.
    /// </summary>
    internal sealed class ExchangeClock
    {
        private const string MarketOverviewEndpoint =
            "https://cdn.tsetmc.com/api/MarketData/GetMarketOverview/0";

        private static readonly TimeSpan HttpTimeout =
            TimeSpan.FromSeconds(
                5);

        private static readonly TimeSpan HttpDateResolutionAllowance =
            TimeSpan.FromMilliseconds(
                500);

        private static readonly TimeZoneInfo TehranTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                "Iran Standard Time");

        private readonly object _stateLock =
            new object();

        private readonly SemaphoreSlim _synchronizationLock =
            new SemaphoreSlim(
                1,
                1);

        private readonly HttpClient _httpClient;

        private bool _hasReading;
        private DateTimeOffset _anchorUtc;
        private long _anchorTimestamp;
        private TimeSpan _roundTripTime;
        private TimeSpan _estimatedUncertainty;

        public const string SourceDisplayName =
            "TSETMC";

        public static TimeSpan SchedulerMaximumSampleAge =>
            TimeSpan.FromSeconds(
                10);

        public static TimeSpan ConfirmationMaximumSampleAge =>
            TimeSpan.FromMinutes(
                2);

        public ExchangeClock()
        {
            HttpClientHandler handler =
                new HttpClientHandler
                {
                    AllowAutoRedirect =
                        false,

                    UseCookies =
                        false
                };

            _httpClient =
                new HttpClient(
                    handler)
                {
                    Timeout =
                        HttpTimeout
                };
        }

        public async Task<ExchangeClockReading> SynchronizeAsync(
            int sampleCount,
            CancellationToken cancellationToken = default)
        {
            if (sampleCount is < 1 or > 5)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleCount));
            }

            await _synchronizationLock.WaitAsync(
                cancellationToken);

            try
            {
                ExchangeClockSample? bestSample =
                    null;

                Exception? lastError =
                    null;

                for (int index = 0;
                    index < sampleCount;
                    index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        ExchangeClockSample sample =
                            await RequestSampleAsync(
                                cancellationToken);

                        if (bestSample == null ||
                            sample.RoundTripTime <
                            bestSample.Value.RoundTripTime)
                        {
                            bestSample =
                                sample;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastError =
                            ex;
                    }
                }

                if (bestSample == null)
                {
                    throw new InvalidOperationException(
                        "No valid TSETMC exchange-clock sample was received.",
                        lastError);
                }

                ExchangeClockSample selectedSample =
                    bestSample.Value;

                if (Stopwatch.GetElapsedTime(
                    selectedSample.ReceiptTimestamp) >
                    SchedulerMaximumSampleAge)
                {
                    throw new InvalidOperationException(
                        "The newest valid TSETMC exchange-clock sample is already stale.");
                }

                lock (_stateLock)
                {
                    _anchorUtc =
                        selectedSample.EstimatedUtcAtReceipt;

                    _anchorTimestamp =
                        selectedSample.ReceiptTimestamp;

                    _roundTripTime =
                        selectedSample.RoundTripTime;

                    _estimatedUncertainty =
                        selectedSample.EstimatedUncertainty;

                    _hasReading =
                        true;
                }

                if (!TryGetReading(
                    TimeSpan.MaxValue,
                    out ExchangeClockReading reading))
                {
                    throw new InvalidOperationException(
                        "The synchronized TSETMC clock could not be read.");
                }

                return reading;
            }
            finally
            {
                _synchronizationLock.Release();
            }
        }

        public bool TryGetReading(
            TimeSpan maximumSampleAge,
            out ExchangeClockReading reading)
        {
            if (maximumSampleAge <
                TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumSampleAge));
            }

            DateTimeOffset anchorUtc;
            long anchorTimestamp;
            TimeSpan roundTripTime;
            TimeSpan estimatedUncertainty;

            lock (_stateLock)
            {
                if (!_hasReading)
                {
                    reading =
                        default;

                    return false;
                }

                anchorUtc =
                    _anchorUtc;

                anchorTimestamp =
                    _anchorTimestamp;

                roundTripTime =
                    _roundTripTime;

                estimatedUncertainty =
                    _estimatedUncertainty;
            }

            TimeSpan sampleAge =
                Stopwatch.GetElapsedTime(
                    anchorTimestamp);

            DateTimeOffset currentUtc =
                anchorUtc +
                sampleAge;

            DateTimeOffset currentTehranTime =
                TimeZoneInfo.ConvertTime(
                    currentUtc,
                    TehranTimeZone);

            reading =
                new ExchangeClockReading(
                    currentTehranTime,
                    sampleAge,
                    roundTripTime,
                    estimatedUncertainty);

            return sampleAge <=
                maximumSampleAge;
        }

        private async Task<ExchangeClockSample> RequestSampleAsync(
            CancellationToken cancellationToken)
        {
            Uri requestUri =
                new Uri(
                    MarketOverviewEndpoint +
                    "?fastOrderClock=" +
                    Guid.NewGuid().ToString(
                        "N"));

            using HttpRequestMessage request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    requestUri);

            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"));

            request.Headers.CacheControl =
                new CacheControlHeaderValue
                {
                    NoCache =
                        true,

                    NoStore =
                        true
                };

            request.Headers.Pragma.ParseAdd(
                "no-cache");

            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 FastOrderExchangeClock/1.0");

            long requestTimestamp =
                Stopwatch.GetTimestamp();

            using HttpResponseMessage response =
                await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                    .ConfigureAwait(
                        false);

            long receiptTimestamp =
                Stopwatch.GetTimestamp();

            response.EnsureSuccessStatusCode();

            if (response.Headers.Age is TimeSpan responseAge &&
                responseAge >
                TimeSpan.FromSeconds(
                    1))
            {
                throw new InvalidOperationException(
                    "TSETMC returned a cached exchange-clock response.");
            }

            DateTimeOffset serverDate =
                response.Headers.Date ??
                throw new InvalidOperationException(
                    "TSETMC did not return an HTTP Date header.");

            string responseBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken)
                    .ConfigureAwait(
                        false);

            using JsonDocument document =
                JsonDocument.Parse(
                    responseBody);

            if (!document.RootElement.TryGetProperty(
                "marketOverview",
                out JsonElement marketOverview) ||
                marketOverview.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "TSETMC returned an unexpected market-overview response.");
            }

            TimeSpan roundTripTime =
                Stopwatch.GetElapsedTime(
                    requestTimestamp,
                    receiptTimestamp);

            TimeSpan estimatedReturnTransit =
                TimeSpan.FromTicks(
                    roundTripTime.Ticks /
                    2);

            DateTimeOffset estimatedUtcAtReceipt =
                serverDate.ToUniversalTime() +
                HttpDateResolutionAllowance +
                estimatedReturnTransit;

            TimeSpan estimatedUncertainty =
                HttpDateResolutionAllowance +
                estimatedReturnTransit;

            return new ExchangeClockSample(
                estimatedUtcAtReceipt,
                receiptTimestamp,
                roundTripTime,
                estimatedUncertainty);
        }

        private readonly record struct ExchangeClockSample(
            DateTimeOffset EstimatedUtcAtReceipt,
            long ReceiptTimestamp,
            TimeSpan RoundTripTime,
            TimeSpan EstimatedUncertainty);
    }
}
