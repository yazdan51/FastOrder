using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastOrder
{
    internal sealed class OfficialOrderUiBridgeResult
    {
        [JsonPropertyName("status")]
        public string Status
        {
            get;
            init;
        } = "";

        [JsonPropertyName("reason")]
        public string Reason
        {
            get;
            init;
        } = "";

        public bool HasStatus(
            string expectedStatus)
        {
            return string.Equals(
                Status,
                expectedStatus,
                StringComparison.Ordinal);
        }
    }

    internal static class OfficialOrderUiBridge
    {
        public const string PreparedStatus =
            "PREPARED";

        public const string ClickedStatus =
            "CLICKED";

        public const string DialogAlreadyOpenStatus =
            "DIALOG_ALREADY_OPEN";

        public const string DialogOpenRequestedStatus =
            "DIALOG_OPEN_REQUESTED";

        public const string SymbolSelectionRequestedStatus =
            "SYMBOL_SELECTION_REQUESTED";

        private const string ExpectedOrigin =
            "https://d.easytrader.ir";

        private const string BridgePropertyName =
            "__fastOrderOfficialUiBridgeV1";

        private static readonly JsonSerializerOptions ResultOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive =
                    true
            };

        public static string BuildEnsureBuyDialogScript(
            Order order)
        {
            ArgumentNullException.ThrowIfNull(
                order);

            string expectedOrigin =
                JsonSerializer.Serialize(
                    ExpectedOrigin);

            string expectedSymbolName =
                JsonSerializer.Serialize(
                    order.SymbolName);

            string expectedSymbolIsin =
                JsonSerializer.Serialize(
                    order.SymbolIsin);

            return $$"""
                (() => {
                    const result = (status, reason) => ({ status, reason });
                    const expectedOrigin = {{expectedOrigin}};
                    const expectedSymbolName = {{expectedSymbolName}};
                    const expectedSymbolIsin = {{expectedSymbolIsin}};
                    const normalizeText = value => String(value ?? "")
                        .replace(/[\s\u200c\u200f\u202a-\u202e]+/g, " ")
                        .trim();
                    const isVisible = element => {
                        if (!(element instanceof HTMLElement)) {
                            return false;
                        }

                        const style = window.getComputedStyle(element);
                        return style.display !== "none" &&
                            style.visibility !== "hidden" &&
                            element.getClientRects().length > 0;
                    };
                    const containsIsin = root => {
                        if (!(root instanceof Element)) {
                            return false;
                        }

                        const expectedIsinUpper = expectedSymbolIsin.toUpperCase();
                        return [root, ...root.querySelectorAll('*')].some(element =>
                            Array.from(element.attributes ?? []).some(attribute =>
                                String(attribute.value ?? "")
                                    .toUpperCase()
                                    .includes(expectedIsinUpper)));
                    };
                    const matchesSymbol = element => {
                        const text = normalizeText(
                            element.textContent ||
                            element.getAttribute('aria-label'));
                        const expected = normalizeText(expectedSymbolName);
                        return text === expected ||
                            text.startsWith(expected + " ");
                    };
                    const findBuyButton = root =>
                        Array.from(root.querySelectorAll('button'))
                            .find(button =>
                                isVisible(button) &&
                                normalizeText(button.textContent) === "خرید");

                    if (window.location.origin !== expectedOrigin) {
                        return result("INVALID_ORIGIN", "EasyTrader origin was not active.");
                    }

                    const openOrderDialog = Array.from(
                        document.querySelectorAll('[role="dialog"], dialog'))
                        .filter(isVisible)
                        .find(candidate =>
                            candidate.querySelector('#quantity') &&
                            candidate.querySelector('#price'));

                    if (openOrderDialog) {
                        const paddedDialogText =
                            " " + normalizeText(openOrderDialog.textContent) + " ";
                        const expected = normalizeText(expectedSymbolName);
                        const symbolMatches =
                            paddedDialogText.includes(" " + expected + " ");

                        if (!symbolMatches ||
                            !containsIsin(openOrderDialog)) {
                            return result(
                                "ACTIVE_DIALOG_MISMATCH",
                                "An order dialog for a different instrument is already open.");
                        }

                        return result(
                            "DIALOG_ALREADY_OPEN",
                            "The matching official buy dialog is already open.");
                    }

                    const expected = normalizeText(expectedSymbolName);
                    const visibleBuyButtons = Array.from(
                        document.querySelectorAll('button'))
                        .filter(button =>
                            isVisible(button) &&
                            normalizeText(button.textContent) === "خرید");

                    for (const buyButton of visibleBuyButtons) {
                        let ancestor = buyButton;

                        for (let depth = 0;
                            depth < 12 && ancestor instanceof HTMLElement;
                            depth += 1, ancestor = ancestor.parentElement) {
                            const hasSymbolHeader =
                                ancestor.querySelector('#symbol-header-last-div') !== null;
                            const hasPriceRange =
                                ancestor.querySelector('#minPrice') !== null;

                            if (!hasSymbolHeader ||
                                !hasPriceRange) {
                                continue;
                            }

                            const paddedPanelText =
                                " " + normalizeText(ancestor.textContent) + " ";

                            if (!paddedPanelText.includes(
                                " " + expected + " ")) {
                                continue;
                            }

                            if (!containsIsin(ancestor)) {
                                return result(
                                    "INSTRUMENT_NOT_VERIFIED",
                                    "ISIN was not present in the selected instrument metadata.");
                            }

                            if (buyButton.disabled ||
                                buyButton.getAttribute('aria-disabled') === 'true') {
                                return result(
                                    "BUY_ACTION_DISABLED",
                                    "The official buy action is disabled.");
                            }

                            buyButton.click();

                            return result(
                                "DIALOG_OPEN_REQUESTED",
                                "The official buy dialog was requested once.");
                        }
                    }

                    const symbolElements = Array.from(
                        document.querySelectorAll('a, [role="link"], [aria-label]'))
                        .filter(element =>
                            isVisible(element) &&
                            matchesSymbol(element));

                    for (const symbolElement of symbolElements) {
                        let ancestor = symbolElement;

                        for (let depth = 0;
                            depth < 10 && ancestor instanceof HTMLElement;
                            depth += 1, ancestor = ancestor.parentElement) {
                            const hasSymbolHeader =
                                ancestor.querySelector('#symbol-header-last-div') !== null;
                            const hasPriceRange =
                                ancestor.querySelector('#minPrice') !== null;
                            const buyButton =
                                findBuyButton(ancestor);

                            if (!hasSymbolHeader ||
                                !hasPriceRange ||
                                !(buyButton instanceof HTMLButtonElement)) {
                                continue;
                            }

                            if (!containsIsin(ancestor)) {
                                return result(
                                    "INSTRUMENT_NOT_VERIFIED",
                                    "ISIN was not present in the selected instrument metadata.");
                            }

                            if (buyButton.disabled ||
                                buyButton.getAttribute('aria-disabled') === 'true') {
                                return result(
                                    "BUY_ACTION_DISABLED",
                                    "The official buy action is disabled.");
                            }

                            buyButton.click();

                            return result(
                                "DIALOG_OPEN_REQUESTED",
                                "The official buy dialog was requested once.");
                        }
                    }

                    const selectableSymbol = symbolElements.find(element =>
                        containsIsin(element));

                    if (selectableSymbol instanceof HTMLElement) {
                        selectableSymbol.click();

                        return result(
                            "SYMBOL_SELECTION_REQUESTED",
                            "The confirmed instrument was selected once.");
                    }

                    return result(
                        "INSTRUMENT_NOT_VISIBLE",
                        "The confirmed instrument was not visible in EasyTrader.");
                })()
                """;
        }

        public static string BuildPrepareScript(
            Order order,
            string nonce)
        {
            ArgumentNullException.ThrowIfNull(
                order);

            if (string.IsNullOrWhiteSpace(
                nonce))
            {
                throw new ArgumentException(
                    "Submission nonce cannot be empty.",
                    nameof(nonce));
            }

            string expectedOrigin =
                JsonSerializer.Serialize(
                    ExpectedOrigin);

            string expectedSymbolName =
                JsonSerializer.Serialize(
                    order.SymbolName);

            string expectedSymbolIsin =
                JsonSerializer.Serialize(
                    order.SymbolIsin);

            string expectedNonce =
                JsonSerializer.Serialize(
                    nonce);

            string bridgePropertyName =
                JsonSerializer.Serialize(
                    BridgePropertyName);

            string expectedQuantity =
                order.Quantity.ToString(
                    CultureInfo.InvariantCulture);

            string expectedPrice =
                order.Price.ToString(
                    CultureInfo.InvariantCulture);

            return $$"""
                (() => {
                    const result = (status, reason) => ({ status, reason });
                    const expectedOrigin = {{expectedOrigin}};
                    const expectedSymbolName = {{expectedSymbolName}};
                    const expectedSymbolIsin = {{expectedSymbolIsin}};
                    const expectedQuantity = "{{expectedQuantity}}";
                    const expectedPrice = "{{expectedPrice}}";
                    const expectedNonce = {{expectedNonce}};
                    const bridgePropertyName = {{bridgePropertyName}};
                    const normalizeText = value => String(value ?? "")
                        .replace(/[\s\u200c\u200f\u202a-\u202e]+/g, " ")
                        .trim();
                    const normalizeNumber = value => String(value ?? "")
                        .replace(/[^0-9]/g, "");
                    const isVisible = element => {
                        if (!(element instanceof HTMLElement)) {
                            return false;
                        }

                        const style = window.getComputedStyle(element);
                        return style.display !== "none" &&
                            style.visibility !== "hidden" &&
                            element.getClientRects().length > 0;
                    };

                    if (window.location.origin !== expectedOrigin) {
                        return result("INVALID_ORIGIN", "EasyTrader origin was not active.");
                    }

                    const dialogs = Array.from(
                        document.querySelectorAll('[role="dialog"], dialog'))
                        .filter(isVisible);
                    const dialog = dialogs.find(candidate =>
                        candidate.querySelector('#quantity') &&
                        candidate.querySelector('#price'));

                    if (!dialog) {
                        return result("ORDER_DIALOG_NOT_FOUND", "Official buy dialog was not found.");
                    }

                    const normalizedExpectedSymbol = normalizeText(expectedSymbolName);
                    const paddedDialogText = " " + normalizeText(dialog.textContent) + " ";
                    const symbolElements = Array.from(
                        dialog.querySelectorAll('a, [role="link"], [aria-label]'))
                        .filter(isVisible);
                    const symbolObserved = symbolElements.some(element => {
                        const text = normalizeText(
                            element.textContent ||
                            element.getAttribute('aria-label'));
                        return text === normalizedExpectedSymbol ||
                            text.startsWith(normalizedExpectedSymbol + " ");
                    }) || paddedDialogText.includes(
                        " " + normalizedExpectedSymbol + " ");

                    if (!symbolObserved) {
                        return result("SYMBOL_MISMATCH", "Visible symbol did not match the confirmed order.");
                    }

                    const expectedIsinUpper = expectedSymbolIsin.toUpperCase();
                    const instrumentElements = [dialog, ...dialog.querySelectorAll('*')];
                    const isinObserved = instrumentElements.some(element =>
                        Array.from(element.attributes ?? []).some(attribute =>
                            String(attribute.value ?? "")
                                .toUpperCase()
                                .includes(expectedIsinUpper)));

                    if (!isinObserved) {
                        return result("INSTRUMENT_NOT_VERIFIED", "ISIN was not present in the official dialog metadata.");
                    }

                    const quantityInput = dialog.querySelector('#quantity');
                    const priceInput = dialog.querySelector('#price');

                    if (!(quantityInput instanceof HTMLInputElement) ||
                        !(priceInput instanceof HTMLInputElement)) {
                        return result("ORDER_INPUTS_NOT_FOUND", "Official order inputs were not available.");
                    }

                    const sendButton = Array.from(dialog.querySelectorAll('button'))
                        .find(button =>
                            isVisible(button) &&
                            normalizeText(button.textContent) === "ارسال خرید");

                    if (!(sendButton instanceof HTMLButtonElement)) {
                        return result("ORDER_ACTION_NOT_FOUND", "Official buy action was not found.");
                    }

                    if (sendButton.disabled ||
                        sendButton.getAttribute('aria-disabled') === 'true') {
                        return result("ORDER_ACTION_DISABLED", "Official buy action was disabled.");
                    }

                    const valueSetter = Object.getOwnPropertyDescriptor(
                        HTMLInputElement.prototype,
                        'value')?.set;

                    if (typeof valueSetter !== 'function') {
                        return result("INPUT_UPDATE_FAILED", "Native input setter was unavailable.");
                    }

                    const setInputValue = (input, value) => {
                        valueSetter.call(input, value);
                        input.dispatchEvent(new Event('input', { bubbles: true }));
                        input.dispatchEvent(new Event('change', { bubbles: true }));
                    };

                    setInputValue(quantityInput, expectedQuantity);
                    setInputValue(priceInput, expectedPrice);

                    if (normalizeNumber(quantityInput.value) !== expectedQuantity ||
                        normalizeNumber(priceInput.value) !== expectedPrice) {
                        return result("INPUT_UPDATE_FAILED", "Official order values did not remain set.");
                    }

                    window[bridgePropertyName] = Object.freeze({
                        nonce: expectedNonce,
                        symbolName: expectedSymbolName,
                        symbolIsin: expectedSymbolIsin,
                        quantity: expectedQuantity,
                        price: expectedPrice
                    });

                    return result("PREPARED", "Official order form is ready for final confirmation.");
                })()
                """;
        }

        public static string BuildSubmitScript(
            Order order,
            string nonce)
        {
            ArgumentNullException.ThrowIfNull(
                order);

            if (string.IsNullOrWhiteSpace(
                nonce))
            {
                throw new ArgumentException(
                    "Submission nonce cannot be empty.",
                    nameof(nonce));
            }

            string expectedOrigin =
                JsonSerializer.Serialize(
                    ExpectedOrigin);

            string expectedSymbolName =
                JsonSerializer.Serialize(
                    order.SymbolName);

            string expectedSymbolIsin =
                JsonSerializer.Serialize(
                    order.SymbolIsin);

            string expectedNonce =
                JsonSerializer.Serialize(
                    nonce);

            string bridgePropertyName =
                JsonSerializer.Serialize(
                    BridgePropertyName);

            string expectedQuantity =
                order.Quantity.ToString(
                    CultureInfo.InvariantCulture);

            string expectedPrice =
                order.Price.ToString(
                    CultureInfo.InvariantCulture);

            return $$"""
                (() => {
                    const result = (status, reason) => ({ status, reason });
                    const expectedOrigin = {{expectedOrigin}};
                    const expectedSymbolName = {{expectedSymbolName}};
                    const expectedSymbolIsin = {{expectedSymbolIsin}};
                    const expectedQuantity = "{{expectedQuantity}}";
                    const expectedPrice = "{{expectedPrice}}";
                    const expectedNonce = {{expectedNonce}};
                    const bridgePropertyName = {{bridgePropertyName}};
                    const normalizeText = value => String(value ?? "")
                        .replace(/[\s\u200c\u200f\u202a-\u202e]+/g, " ")
                        .trim();
                    const normalizeNumber = value => String(value ?? "")
                        .replace(/[^0-9]/g, "");
                    const isVisible = element => {
                        if (!(element instanceof HTMLElement)) {
                            return false;
                        }

                        const style = window.getComputedStyle(element);
                        return style.display !== "none" &&
                            style.visibility !== "hidden" &&
                            element.getClientRects().length > 0;
                    };

                    if (window.location.origin !== expectedOrigin) {
                        return result("INVALID_ORIGIN", "EasyTrader origin was not active.");
                    }

                    const prepared = window[bridgePropertyName];

                    if (!prepared ||
                        prepared.nonce !== expectedNonce ||
                        prepared.symbolName !== expectedSymbolName ||
                        prepared.symbolIsin !== expectedSymbolIsin ||
                        prepared.quantity !== expectedQuantity ||
                        prepared.price !== expectedPrice) {
                        return result("PREPARATION_EXPIRED", "Prepared official form state was no longer valid.");
                    }

                    const dialogs = Array.from(
                        document.querySelectorAll('[role="dialog"], dialog'))
                        .filter(isVisible);
                    const dialog = dialogs.find(candidate =>
                        candidate.querySelector('#quantity') &&
                        candidate.querySelector('#price'));

                    if (!dialog) {
                        return result("ORDER_DIALOG_NOT_FOUND", "Official buy dialog was not found.");
                    }

                    const normalizedExpectedSymbol = normalizeText(expectedSymbolName);
                    const paddedDialogText = " " + normalizeText(dialog.textContent) + " ";
                    const symbolElements = Array.from(
                        dialog.querySelectorAll('a, [role="link"], [aria-label]'))
                        .filter(isVisible);
                    const symbolObserved = symbolElements.some(element => {
                        const text = normalizeText(
                            element.textContent ||
                            element.getAttribute('aria-label'));
                        return text === normalizedExpectedSymbol ||
                            text.startsWith(normalizedExpectedSymbol + " ");
                    }) || paddedDialogText.includes(
                        " " + normalizedExpectedSymbol + " ");

                    if (!symbolObserved) {
                        return result("SYMBOL_MISMATCH", "Visible symbol did not match the confirmed order.");
                    }

                    const expectedIsinUpper = expectedSymbolIsin.toUpperCase();
                    const instrumentElements = [dialog, ...dialog.querySelectorAll('*')];
                    const isinObserved = instrumentElements.some(element =>
                        Array.from(element.attributes ?? []).some(attribute =>
                            String(attribute.value ?? "")
                                .toUpperCase()
                                .includes(expectedIsinUpper)));

                    if (!isinObserved) {
                        return result("INSTRUMENT_NOT_VERIFIED", "ISIN was not present in the official dialog metadata.");
                    }

                    const quantityInput = dialog.querySelector('#quantity');
                    const priceInput = dialog.querySelector('#price');

                    if (!(quantityInput instanceof HTMLInputElement) ||
                        !(priceInput instanceof HTMLInputElement) ||
                        normalizeNumber(quantityInput.value) !== expectedQuantity ||
                        normalizeNumber(priceInput.value) !== expectedPrice) {
                        return result("ORDER_VALUES_CHANGED", "Official order values changed after preparation.");
                    }

                    const sendButton = Array.from(dialog.querySelectorAll('button'))
                        .find(button =>
                            isVisible(button) &&
                            normalizeText(button.textContent) === "ارسال خرید");

                    if (!(sendButton instanceof HTMLButtonElement)) {
                        return result("ORDER_ACTION_NOT_FOUND", "Official buy action was not found.");
                    }

                    if (sendButton.disabled ||
                        sendButton.getAttribute('aria-disabled') === 'true') {
                        return result("ORDER_ACTION_DISABLED", "Official buy action was disabled.");
                    }

                    delete window[bridgePropertyName];
                    sendButton.click();

                    return result("CLICKED", "Official buy action was invoked once.");
                })()
                """;
        }

        public static string BuildClearScript(
            string nonce)
        {
            if (string.IsNullOrWhiteSpace(
                nonce))
            {
                throw new ArgumentException(
                    "Submission nonce cannot be empty.",
                    nameof(nonce));
            }

            string expectedNonce =
                JsonSerializer.Serialize(
                    nonce);

            string bridgePropertyName =
                JsonSerializer.Serialize(
                    BridgePropertyName);

            return $$"""
                (() => {
                    const bridgePropertyName = {{bridgePropertyName}};
                    const prepared = window[bridgePropertyName];

                    if (prepared && prepared.nonce === {{expectedNonce}}) {
                        delete window[bridgePropertyName];
                    }

                    return true;
                })()
                """;
        }

        public static OfficialOrderUiBridgeResult ParseResult(
            string resultJson)
        {
            if (string.IsNullOrWhiteSpace(
                resultJson))
            {
                return InvalidResult();
            }

            try
            {
                OfficialOrderUiBridgeResult? result =
                    JsonSerializer.Deserialize<OfficialOrderUiBridgeResult>(
                        resultJson,
                        ResultOptions);

                return result == null ||
                    string.IsNullOrWhiteSpace(
                        result.Status)
                    ? InvalidResult()
                    : result;
            }
            catch (JsonException)
            {
                return InvalidResult();
            }
        }

        public static string GetUserMessage(
            string status)
        {
            return status switch
            {
                "INVALID_ORIGIN" =>
                    "صفحه فعال متعلق به EasyTrader نیست.",

                "ORDER_DIALOG_NOT_FOUND" =>
                    "پنجره رسمی خرید به‌طور خودکار باز نشد.",

                "ACTIVE_DIALOG_MISMATCH" =>
                    "پنجره سفارش ابزار دیگری باز است؛ آن را ببندید و دوباره تلاش کنید.",

                "BUY_ACTION_DISABLED" =>
                    "دکمه رسمی خرید در EasyTrader غیرفعال است.",

                "INSTRUMENT_NOT_VISIBLE" =>
                    "نماد تأییدشده در صفحه فعلی EasyTrader قابل انتخاب نیست.",

                "ORDER_DIALOG_OPEN_TIMEOUT" =>
                    "پنجره رسمی خرید در مهلت مقرر باز نشد.",

                "SYMBOL_MISMATCH" =>
                    "نماد پنجره رسمی با سفارش تأییدشده مطابقت ندارد.",

                "INSTRUMENT_NOT_VERIFIED" =>
                    "شناسه ابزار مالی در پنجره رسمی قابل تطبیق نبود؛ ارسال متوقف شد.",

                "ORDER_INPUTS_NOT_FOUND" =>
                    "ورودی‌های رسمی قیمت و تعداد پیدا نشدند.",

                "ORDER_ACTION_NOT_FOUND" =>
                    "دکمه رسمی ارسال خرید پیدا نشد.",

                "ORDER_ACTION_DISABLED" =>
                    "دکمه رسمی ارسال خرید در EasyTrader غیرفعال است.",

                "INPUT_UPDATE_FAILED" =>
                    "مقادیر فرم رسمی به‌طور قابل‌اعتماد تنظیم نشدند.",

                "PREPARATION_EXPIRED" =>
                    "وضعیت آماده‌شده فرم رسمی تغییر کرده یا منقضی شده است.",

                "ORDER_VALUES_CHANGED" =>
                    "قیمت یا تعداد پس از تأیید تغییر کرده است.",

                _ =>
                    "پاسخ مسیر رسمی EasyTrader قابل تأیید نبود."
            };
        }

        private static OfficialOrderUiBridgeResult InvalidResult()
        {
            return new OfficialOrderUiBridgeResult
            {
                Status =
                    "SCRIPT_RESULT_INVALID",

                Reason =
                    "Official UI bridge returned an invalid result."
            };
        }
    }
}
