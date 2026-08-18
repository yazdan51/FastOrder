
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FastOrder
{
    public class OrderService
    {
        private const string BaseUrl =
            "https://api-mts.orbis.easytrader.ir";

        private const string OrderEndpoint =
            "/core/api/v2/order";

        private readonly string _accessToken;

        public OrderService(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException(
                    "Access Token is empty.",
                    nameof(accessToken));

            _accessToken = accessToken;
        }

        // =====================================================
        // SEND ORDER
        // =====================================================

        public async Task<string> SendOrderAsync(
            CreateOrderPayload payload)
        {
            try
            {
                using HttpClient client = new HttpClient();

                client.BaseAddress =
                    new Uri(BaseUrl);

                // -------------------------------------------------
                // Authorization
                // -------------------------------------------------

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        _accessToken);

                // -------------------------------------------------
                // Headers مشابه درخواست موفق Chrome
                // -------------------------------------------------

                client.DefaultRequestHeaders.Accept.Clear();

                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/json"));

                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "text/plain"));

                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "*/*"));

                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "Accept-Language",
                    "fa");

                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "Origin",
                    "https://d.easytrader.ir");

                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "Referer",
                    "https://d.easytrader.ir/");

                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                    "Chrome/151.0.0.0 Safari/537.36");

                // -------------------------------------------------
                // JSON
                // -------------------------------------------------

                JsonSerializerOptions options =
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy =
                            JsonNamingPolicy.CamelCase,

                        Encoder =
                            System.Text.Encodings
                                .Web.JavaScriptEncoder
                                .UnsafeRelaxedJsonEscaping,

                        WriteIndented = true
                    };

                string json =
                    JsonSerializer.Serialize(
                        payload,
                        options);

                using StringContent content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json");

                // -------------------------------------------------
                // LOG REQUEST
                // -------------------------------------------------

                StringBuilder result =
                    new StringBuilder();

                result.AppendLine(
                    "========================================");

                result.AppendLine(
                    "SEND ORDER");

                result.AppendLine(
                    "========================================");

                result.AppendLine(
                    "Request URL:");

                result.AppendLine(
                    BaseUrl + OrderEndpoint);

                result.AppendLine();

                result.AppendLine(
                    "Request Method:");

                result.AppendLine("POST");

                result.AppendLine();

                result.AppendLine(
                    "Request JSON:");

                result.AppendLine(json);

                result.AppendLine();

                // -------------------------------------------------
                // POST
                // -------------------------------------------------

                using HttpResponseMessage response =
                    await client.PostAsync(
                        OrderEndpoint,
                        content);

                string responseText =
                    await response.Content.ReadAsStringAsync();

                result.AppendLine(
                    "HTTP Status:");

                result.AppendLine(
                    $"{(int)response.StatusCode} " +
                    $"{response.StatusCode}");

                result.AppendLine();

                result.AppendLine(
                    "Response:");

                result.AppendLine(responseText);

                result.AppendLine();

                // =================================================
                // HTTP ERROR
                // =================================================

                if (!response.IsSuccessStatusCode)
                {
                    result.AppendLine(
                        "----------------------------------------");

                    result.AppendLine(
                        "HTTP REQUEST FAILED");

                    result.AppendLine(
                        "----------------------------------------");

                    result.AppendLine(
                        $"Status Code: {(int)response.StatusCode}");

                    result.AppendLine(
                        $"Reason: {response.ReasonPhrase}");

                    return result.ToString();
                }

                // =================================================
                // EMPTY RESPONSE
                // =================================================

                if (string.IsNullOrWhiteSpace(responseText))
                {
                    result.AppendLine(
                        "----------------------------------------");

                    result.AppendLine(
                        "EMPTY RESPONSE");

                    return result.ToString();
                }

                // =================================================
                // PARSE JSON RESPONSE
                // =================================================

                try
                {
                    using JsonDocument document =
                        JsonDocument.Parse(responseText);

                    JsonElement root =
                        document.RootElement;

                    bool isSuccessful = false;

                    if (root.TryGetProperty(
                        "isSuccessful",
                        out JsonElement successElement))
                    {
                        if (successElement.ValueKind ==
                            JsonValueKind.True ||
                            successElement.ValueKind ==
                            JsonValueKind.False)
                        {
                            isSuccessful =
                                successElement.GetBoolean();
                        }
                    }

                    // =================================================
                    // ORDER SUCCESS
                    // =================================================

                    if (isSuccessful)
                    {
                        string orderId = "";

                        if (root.TryGetProperty(
                            "id",
                            out JsonElement idElement))
                        {
                            orderId =
                                idElement.GetString() ?? "";
                        }

                        result.AppendLine(
                            "----------------------------------------");

                        result.AppendLine(
                            "ORDER SUCCESS");

                        result.AppendLine(
                            "----------------------------------------");

                        result.AppendLine(
                            $"Order ID: {orderId}");

                        return result.ToString();
                    }

                    // =================================================
                    // ORDER REJECTED BY OMS
                    // =================================================

                    string message = "";

                    if (root.TryGetProperty(
                        "message",
                        out JsonElement messageElement))
                    {
                        message =
                            messageElement.GetString() ?? "";
                    }

                    string omsCode = "";

                    string omsName = "";

                    string omsError = "";

                    if (root.TryGetProperty(
                        "omsError",
                        out JsonElement omsErrorElement))
                    {
                        if (omsErrorElement.ValueKind ==
                            JsonValueKind.Array &&
                            omsErrorElement.GetArrayLength() > 0)
                        {
                            JsonElement firstError =
                                omsErrorElement[0];

                            if (firstError.TryGetProperty(
                                "code",
                                out JsonElement codeElement))
                            {
                                omsCode =
                                    codeElement.ToString();
                            }

                            if (firstError.TryGetProperty(
                                "name",
                                out JsonElement nameElement))
                            {
                                omsName =
                                    nameElement.GetString() ?? "";
                            }

                            if (firstError.TryGetProperty(
                                "error",
                                out JsonElement errorElement))
                            {
                                omsError =
                                    errorElement.GetString() ?? "";
                            }
                        }
                    }

                    result.AppendLine(
                        "----------------------------------------");

                    result.AppendLine(
                        "ORDER REJECTED BY OMS");

                    result.AppendLine(
                        "----------------------------------------");

                    if (!string.IsNullOrWhiteSpace(omsCode))
                    {
                        result.AppendLine(
                            $"OMS Code: {omsCode}");
                    }

                    if (!string.IsNullOrWhiteSpace(omsName))
                    {
                        result.AppendLine(
                            $"OMS Name: {omsName}");
                    }

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        result.AppendLine(
                            $"Message: {message}");
                    }

                    if (!string.IsNullOrWhiteSpace(omsError))
                    {
                        result.AppendLine(
                            $"OMS Error: {omsError}");
                    }

                    return result.ToString();
                }
                catch (JsonException jsonException)
                {
                    result.AppendLine(
                        "----------------------------------------");

                    result.AppendLine(
                        "JSON PARSE ERROR");

                    result.AppendLine(
                        jsonException.Message);

                    return result.ToString();
                }
            }
            catch (HttpRequestException httpException)
            {
                return
                    "========================================\r\n" +
                    "HTTP REQUEST ERROR\r\n" +
                    "========================================\r\n" +
                    httpException.Message;
            }
            catch (TaskCanceledException)
            {
                return
                    "========================================\r\n" +
                    "REQUEST TIMEOUT\r\n" +
                    "========================================";
            }
            catch (Exception exception)
            {
                return
                    "========================================\r\n" +
                    "GENERAL ERROR\r\n" +
                    "========================================\r\n" +
                    exception;
            }
        }

        // =====================================================
        // TEST CONNECTION
        // =====================================================

        public async Task<string> TestConnectionAsync()
        {
            using HttpClient client =
                new HttpClient();

            client.BaseAddress =
                new Uri(BaseUrl);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    _accessToken);

            client.DefaultRequestHeaders.Accept.Clear();

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"));

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Origin",
                "https://d.easytrader.ir");

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Referer",
                "https://d.easytrader.ir/");

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/151.0.0.0 Safari/537.36");

            using HttpResponseMessage response =
                await client.GetAsync(
                    "/core/api/order");

            string responseText =
                await response.Content.ReadAsStringAsync();

            return
                $"HTTP {(int)response.StatusCode} " +
                $"{response.StatusCode}\r\n\r\n" +
                responseText;
        }
    }
}

