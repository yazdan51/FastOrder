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

    internal sealed class OfficialOrderFormReadResult
    {
        [JsonPropertyName("status")]
        public string Status { get; init; } = "";

        [JsonPropertyName("reason")]
        public string Reason { get; init; } = "";

        [JsonPropertyName("symbolName")]
        public string SymbolName { get; init; } = "";

        [JsonPropertyName("symbolIsin")]
        public string SymbolIsin { get; init; } = "";

        [JsonPropertyName("price")]
        public string Price { get; init; } = "";

        [JsonPropertyName("quantity")]
        public string Quantity { get; init; } = "";

        [JsonPropertyName("side")]
        public int Side { get; init; }

        [JsonPropertyName("commissionAmount")]
        public string CommissionAmount { get; init; } = "";

        [JsonPropertyName("totalValue")]
        public string TotalValue { get; init; } = "";

        public bool HasStatus(string expectedStatus) =>
            string.Equals(Status, expectedStatus, StringComparison.Ordinal);
    }

    internal static class OfficialOrderUiBridge
    {
        public const string FormReadStatus =
            "FORM_READ";

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

        public static string BuildOpenCurrentSymbolBuyDialogScript()
        {
            string expectedOrigin = JsonSerializer.Serialize(ExpectedOrigin);

            return $$"""
                (() => {
                    const result = (status, reason) => ({ status, reason });
                    const expectedOrigin = {{expectedOrigin}};
                    const metaKey = "__fastOrderCurrentInstrumentV1";
                    const norm = v => String(v ?? "")
                        .replace(/[\s\u200c\u200f\u202a-\u202e]+/g, " ").trim();
                    const visible = e => {
                        if (!(e instanceof HTMLElement)) return false;
                        const s = getComputedStyle(e);
                        return s.display !== "none" &&
                            s.visibility !== "hidden" &&
                            e.getClientRects().length > 0;
                    };
                    const isinFrom = root => {
                        if (!(root instanceof Element)) return "";
                        const all = [root, ...root.querySelectorAll("*")];
                        for (const e of all) {
                            for (const a of Array.from(e.attributes ?? [])) {
                                const m = String(a.value ?? "").toUpperCase()
                                    .match(/IR[A-Z0-9]{10}/);
                                if (m) return m[0];
                            }
                        }
                        return "";
                    };
                    const nameFrom = root => {
                        const direct = [
                            "[data-symbol-name]", "[symbol-name]",
                            "#symbol-name", ".symbol-name"
                        ];
                        for (const sel of direct) {
                            const e = root.querySelector(sel);
                            if (e instanceof HTMLElement && visible(e)) {
                                const v = norm(
                                    e.getAttribute("data-symbol-name") ||
                                    e.getAttribute("symbol-name") ||
                                    e.textContent);
                                if (v && v.length <= 40) return v;
                            }
                        }
                        const candidates = Array.from(
                            root.querySelectorAll("a,[role=\"link\"],[aria-label]"))
                            .filter(visible)
                            .map(e => norm(e.getAttribute("aria-label") || e.textContent))
                            .filter(v => v && v.length <= 40 &&
                                !/^[0-9,.\s]+$/.test(v) &&
                                v !== "خرید" && v !== "فروش");
                        const raw = candidates[0] || "";

                        if (raw.includes("TAL")) {
                            const beforeTal = norm(raw.split("TAL")[0]);
                            if (beforeTal) return beforeTal;
                        }

                        return raw;
                    };

                    if (location.origin !== expectedOrigin)
                        return result("INVALID_ORIGIN","EasyTrader origin was not active.");

                    const openDialog = Array.from(
                        document.querySelectorAll('[role="dialog"],dialog'))
                        .filter(visible)
                        .find(d => d.querySelector("#quantity") && d.querySelector("#price"));

                    if (openDialog)
                        return result("DIALOG_ALREADY_OPEN","An official order dialog is already open.");

                    const buys = Array.from(document.querySelectorAll("button"))
                        .filter(b => visible(b) && norm(b.textContent) === "خرید");

                    for (const buy of buys) {
                        let a = buy;
                        for (let depth=0; depth<12 && a instanceof HTMLElement;
                             depth++, a=a.parentElement) {
                            if (a===document.body || a===document.documentElement) break;
                            if (!a.querySelector("#symbol-header-last-div") ||
                                !a.querySelector("#minPrice")) continue;

                            const symbolIsin = isinFrom(a);
                            const symbolName = nameFrom(a);

                            if (!symbolIsin)
                                return result("INSTRUMENT_NOT_VERIFIED","Current ISIN was not found.");
                            if (!symbolName)
                                return result("SYMBOL_NAME_NOT_FOUND","Current symbol name was not found.");
                            if (buy.disabled || buy.getAttribute("aria-disabled")==="true")
                                return result("BUY_ACTION_DISABLED","Buy action is disabled.");

                            window[metaKey] = Object.freeze({
                                symbolName, symbolIsin, capturedAt: Date.now()
                            });

                            buy.click();
                            return result("DIALOG_OPEN_REQUESTED","Current buy dialog requested.");
                        }
                    }

                    return result("CURRENT_INSTRUMENT_NOT_FOUND","Visible current instrument was not found.");
                })()
                """;
        }

        public static string BuildReadCurrentOrderFormScript()
        {
            string expectedOrigin = JsonSerializer.Serialize(ExpectedOrigin);

            return $$"""
                (() => {
                    const result = (status, reason, symbolName="", symbolIsin="",
                                    price="", quantity="", side=0,
                                    commissionAmount="", totalValue="") =>
                        ({status, reason, symbolName, symbolIsin, price, quantity, side,
                          commissionAmount, totalValue});
                    const expectedOrigin = {{expectedOrigin}};
                    const metaKey = "__fastOrderCurrentInstrumentV1";
                    const norm = v => String(v ?? "")
                        .replace(/[\s\u200c\u200f\u202a-\u202e]+/g," ").trim();
                    const num = v => String(v ?? "")
                        .replace(/[۰-۹]/g,c=>String(c.charCodeAt(0)-"۰".charCodeAt(0)))
                        .replace(/[٠-٩]/g,c=>String(c.charCodeAt(0)-"٠".charCodeAt(0)))
                        .replace(/[^0-9]/g,"");
                    const visible = e => {
                        if (!(e instanceof HTMLElement)) return false;
                        const s=getComputedStyle(e);
                        return s.display!=="none" && s.visibility!=="hidden" &&
                            e.getClientRects().length>0;
                    };
                    const labeledAmount = (root, labels, attrRegex) => {
                        if (!(root instanceof Element)) return "";

                        const elements = Array.from(root.querySelectorAll("*"))
                            .filter(visible);

                        const exactLabels = elements.filter(e => {
                            const text = norm(e.textContent)
                                .replace(/[:：]/g, "")
                                .trim();

                            return labels.includes(text);
                        });

                        const pickNumeric = scope => {
                            if (!(scope instanceof Element)) return "";

                            const numericElements = [scope, ...scope.querySelectorAll("*")]
                                .filter(visible)
                                .map(e => ({
                                    element: e,
                                    text: norm(e.textContent)
                                }))
                                .filter(x =>
                                    x.text &&
                                    !labels.includes(
                                        x.text.replace(/[:：]/g, "").trim()) &&
                                    /[0-9۰-۹٠-٩]/.test(x.text));

                            for (const item of numericElements) {
                                const v = num(item.text);
                                if (v && Number(v) > 0) return v;
                            }

                            return "";
                        };

                        for (const label of exactLabels) {
                            const siblingScopes = [
                                label.nextElementSibling,
                                label.previousElementSibling
                            ].filter(Boolean);

                            for (const scope of siblingScopes) {
                                const v = pickNumeric(scope);
                                if (v) return v;
                            }

                            let row = label.parentElement;

                            for (let depth = 0;
                                depth < 4 && row instanceof HTMLElement;
                                depth++, row = row.parentElement) {

                                const rowText = norm(row.textContent)
                                    .replace(/[:：]/g, " ")
                                    .trim();

                                if (!labels.some(x => rowText.includes(x))) {
                                    continue;
                                }

                                const candidates = Array.from(row.children)
                                    .filter(visible)
                                    .filter(child => child !== label);

                                for (const candidate of candidates) {
                                    const v = pickNumeric(candidate);
                                    if (v) return v;
                                }
                            }
                        }

                        for (const e of elements) {
                            const hasSemanticAttribute =
                                Array.from(e.attributes ?? []).some(a =>
                                    attrRegex.test(
                                        String(a.name) + " " + String(a.value)));

                            if (!hasSemanticAttribute) continue;

                            for (const raw of [
                                e.getAttribute("value"),
                                e.getAttribute("data-value"),
                                e.getAttribute("data-total"),
                                e.getAttribute("data-amount")
                            ]) {
                                const v = num(raw);
                                if (v && Number(v) > 0) return v;
                            }
                        }

                        return "";
                    };

                    const summaryAmount = (root, exactLabel, directSelector="") => {
                        if (!(root instanceof Element)) return "";

                        if (directSelector) {
                            const direct = root.querySelector(directSelector);
                            if (direct instanceof HTMLElement && visible(direct)) {
                                const v = num(direct.textContent);
                                if (v && Number(v) > 0) return v;
                            }
                        }

                        const rows = Array.from(
                            root.querySelectorAll("order-form-summary .summary-item, .summary-item"))
                            .filter(visible);

                        for (const row of rows) {
                            const label = Array.from(row.querySelectorAll("span"))
                                .filter(visible)
                                .find(s => norm(s.textContent)
                                    .replace(/[:：]/g, "")
                                    .trim() === exactLabel);

                            if (!(label instanceof HTMLElement)) continue;

                            const values = Array.from(row.querySelectorAll("span"))
                                .filter(visible)
                                .filter(s => s !== label);

                            for (const value of values) {
                                const v = num(value.textContent);
                                if (v && Number(v) > 0) return v;
                            }

                            const rowText = norm(row.textContent);
                            const withoutLabel = rowText.replace(exactLabel, " ");
                            const v = num(withoutLabel);
                            if (v && Number(v) > 0) return v;
                        }

                        return "";
                    };

                    const isinFrom = root => {
                        if (!(root instanceof Element)) return "";
                        for (const e of [root,...root.querySelectorAll("*")]) {
                            for (const a of Array.from(e.attributes ?? [])) {
                                const m=String(a.value??"").toUpperCase()
                                    .match(/IR[A-Z0-9]{10}/);
                                if (m) return m[0];
                            }
                        }
                        return "";
                    };

                    if (location.origin !== expectedOrigin)
                        return result("INVALID_ORIGIN","EasyTrader origin was not active.");

                    let box = Array.from(
                        document.querySelectorAll('[role="dialog"],dialog'))
                        .filter(visible)
                        .find(d=>d.querySelector("#quantity") && d.querySelector("#price"));

                    if (!box) {
                        for (const q of Array.from(document.querySelectorAll("#quantity")).filter(visible)) {
                            let a=q.parentElement;
                            for (let depth=0; depth<12 && a instanceof HTMLElement;
                                 depth++, a=a.parentElement) {
                                if (a===document.body || a===document.documentElement) break;
                                const p=a.querySelector("#price");
                                const send=Array.from(a.querySelectorAll("button"))
                                    .find(b=>visible(b) &&
                                        ["ارسال خرید","ارسال فروش"].includes(norm(b.textContent)));
                                if (p && send) { box=a; break; }
                            }
                            if (box) break;
                        }
                    }

                    if (!(box instanceof HTMLElement))
                        return result("ORDER_FORM_NOT_FOUND","Official order form not found.");

                    const q=box.querySelector("#quantity");
                    const p=box.querySelector("#price");
                    if (!(q instanceof HTMLInputElement) || !(p instanceof HTMLInputElement))
                        return result("ORDER_INPUTS_NOT_FOUND","Order inputs not found.");

                    const quantity=num(q.value);
                    const price=num(p.value);
                    if (!quantity || Number(quantity)<=0)
                        return result("QUANTITY_NOT_READY","Quantity invalid.");
                    if (!price || Number(price)<=0)
                        return result("PRICE_NOT_READY","Price invalid.");

                    const send=Array.from(box.querySelectorAll("button"))
                        .find(b=>visible(b) &&
                            ["ارسال خرید","ارسال فروش"].includes(norm(b.textContent)));
                    if (!(send instanceof HTMLButtonElement))
                        return result("SEND_ACTION_NOT_FOUND","Send action not found.");

                    const side=norm(send.textContent)==="ارسال فروش" ? 1 : 0;
                    const meta=window[metaKey] || {};
                    const symbolName=norm(meta.symbolName || "");
                    const symbolIsin=String(meta.symbolIsin || isinFrom(box) || "").toUpperCase();

                    if (!symbolName || !symbolIsin)
                        return result("INSTRUMENT_METADATA_NOT_READY","Instrument metadata not ready.");

                    const commissionAmount =
                        summaryAmount(box, "کارمزد معامله") ||
                        labeledAmount(
                            box,
                            ["کارمزد معامله", "کارمزد", "مبلغ کارمزد"],
                            /commission|fee|کارمزد/i);

                    if (!commissionAmount || Number(commissionAmount) <= 0) {
                        const style = getComputedStyle(box);
                        const rect = box.getBoundingClientRect();

                        const interesting = Array.from(box.querySelectorAll("*"))
                            .filter(e => e instanceof HTMLElement)
                            .map(e => {
                                const text = norm(e.textContent);
                                const attrs = Array.from(e.attributes ?? [])
                                    .map(a => `${a.name}=${a.value}`)
                                    .join(" ");
                                if (!/کارمزد|جمع کل|ارزش|مبلغ|commission|fee|total|value/i
                                    .test(text + " " + attrs)) {
                                    return null;
                                }

                                const r = e.getBoundingClientRect();
                                const s = getComputedStyle(e);

                                return {
                                    tag: e.tagName,
                                    text: text.slice(0, 250),
                                    attrs: attrs.slice(0, 400),
                                    display: s.display,
                                    visibility: s.visibility,
                                    opacity: s.opacity,
                                    zIndex: s.zIndex,
                                    rect: {
                                        x: Math.round(r.x),
                                        y: Math.round(r.y),
                                        w: Math.round(r.width),
                                        h: Math.round(r.height)
                                    }
                                };
                            })
                            .filter(Boolean)
                            .slice(0, 30);

                        const diagnostic = {
                            box: {
                                text: norm(box.innerText).slice(0, 2500),
                                display: style.display,
                                visibility: style.visibility,
                                opacity: style.opacity,
                                zIndex: style.zIndex,
                                pointerEvents: style.pointerEvents,
                                rect: {
                                    x: Math.round(rect.x),
                                    y: Math.round(rect.y),
                                    w: Math.round(rect.width),
                                    h: Math.round(rect.height)
                                }
                            },
                            interesting
                        };

                        return result("COMMISSION_NOT_READY",
                            "Commission amount not found. DOM_DIAGNOSTIC=" +
                            JSON.stringify(diagnostic).slice(0, 7000),
                            symbolName,symbolIsin,price,quantity,side,"","");
                    }

                    const totalValue =
                        summaryAmount(
                            box,
                            "جمع کل",
                            '[data-cy="order-summary-total-expense"]') ||
                        labeledAmount(
                            box,
                            ["جمع کل", "ارزش نهایی", "مبلغ نهایی", "ارزش سفارش",
                             "مبلغ سفارش", "ارزش کل", "مبلغ کل",
                             "ارزش معامله", "مبلغ قابل پرداخت", "قابل پرداخت", "جمع سفارش"],
                            /total|value|amount|payable|ارزش|مبلغ/i);

                    if (!totalValue || Number(totalValue) <= 0)
                        return result("TOTAL_VALUE_NOT_READY",
                            "Total order value not found.",
                            symbolName,symbolIsin,price,quantity,side,
                            commissionAmount,"");

                    return result("FORM_READ","Official form read without submission.",
                        symbolName,symbolIsin,price,quantity,side,
                        commissionAmount,totalValue);
                })()
                """;
        }

        /// <summary>
        /// اسکریپتی می‌سازد که فقط در دامنه رسمی EasyTrader، نماد و ISIN
        /// تأییدشده را پیدا می‌کند و در صورت نیاز پنجره رسمی خرید را باز می‌کند.
        /// این مرحله دکمه «ارسال خرید» را فعال نمی‌کند و POST نمی‌فرستد.
        /// </summary>
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
                    const dialogRequestProperty =
                        "__fastOrderBuyDialogRequestV1";
                    const requestBuyDialogOpen = buyButton => {
                        if (!(buyButton instanceof HTMLButtonElement)) {
                            return result(
                                "BUY_ACTION_NOT_FOUND",
                                "The official buy action was not found.");
                        }

                        if (buyButton.disabled ||
                            buyButton.getAttribute('aria-disabled') === 'true') {
                            return result(
                                "BUY_ACTION_DISABLED",
                                "The official buy action is disabled.");
                        }

                        const now = Date.now();
                        const previousRequest =
                            window[dialogRequestProperty];

                        if (previousRequest &&
                            previousRequest.symbolIsin === expectedSymbolIsin &&
                            now - previousRequest.requestedAt < 1500) {
                            return result(
                                "DIALOG_OPEN_REQUESTED",
                                "The official buy dialog request is still pending.");
                        }

                        window[dialogRequestProperty] = Object.freeze({
                            symbolIsin: expectedSymbolIsin,
                            requestedAt: now
                        });

                        buyButton.click();

                        return result(
                            "DIALOG_OPEN_REQUESTED",
                            "The official buy dialog was requested once.");
                    };

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
                        delete window[dialogRequestProperty];

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

                            return requestBuyDialogOpen(
                                buyButton);
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

                            return requestBuyDialogOpen(
                                buyButton);
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

        /// <summary>
        /// اسکریپت آماده‌سازی فرم رسمی را می‌سازد. نماد، ISIN، ورودی‌ها و دکمه
        /// رسمی بررسی می‌شوند؛ سپس تعداد و قیمت تنظیم و با Nonce موقت قفل می‌شوند.
        /// خروجی PREPARED به معنی آمادگی فرم است، نه ارسال سفارش.
        /// </summary>
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
                    const findOrderContainer = () => {
                        const candidates = Array.from(
                            document.querySelectorAll('[role="dialog"], dialog'))
                            .filter(candidate =>
                                isVisible(candidate) &&
                                candidate.querySelector('#quantity') &&
                                candidate.querySelector('#price'));

                        const quantityInputs = Array.from(
                            document.querySelectorAll('#quantity'))
                            .filter(isVisible);

                        for (const quantityInput of quantityInputs) {
                            let ancestor = quantityInput.parentElement;

                            for (let depth = 0;
                                depth < 12 && ancestor instanceof HTMLElement;
                                depth += 1, ancestor = ancestor.parentElement) {
                                if (ancestor === document.body ||
                                    ancestor === document.documentElement) {
                                    break;
                                }

                                const priceInput =
                                    ancestor.querySelector('#price');
                                const sendButton = Array.from(
                                    ancestor.querySelectorAll('button'))
                                    .find(button =>
                                        isVisible(button) &&
                                        normalizeText(button.textContent) === "ارسال خرید");

                                if (priceInput &&
                                    sendButton &&
                                    !candidates.includes(ancestor)) {
                                    candidates.push(ancestor);
                                }
                            }
                        }

                        const expected = normalizeText(expectedSymbolName);
                        const expectedIsinUpper = expectedSymbolIsin.toUpperCase();

                        return candidates.find(candidate => {
                            const paddedText =
                                " " + normalizeText(candidate.textContent) + " ";
                            const symbolMatches =
                                paddedText.includes(" " + expected + " ");
                            const isinMatches = [candidate, ...candidate.querySelectorAll('*')]
                                .some(element =>
                                    Array.from(element.attributes ?? []).some(attribute =>
                                        String(attribute.value ?? "")
                                            .toUpperCase()
                                            .includes(expectedIsinUpper)));

                            return symbolMatches &&
                                isinMatches;
                        }) || candidates[0] || null;
                    };

                    if (window.location.origin !== expectedOrigin) {
                        return result("INVALID_ORIGIN", "EasyTrader origin was not active.");
                    }

                    const dialog =
                        findOrderContainer();

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

                    // فقط داده لازم برای تطبیق همین تلاش در حافظه صفحه نگه داشته می‌شود.
                    // Token، Cookie و Header احراز هویت خوانده یا ذخیره نمی‌شوند.
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

        /// <summary>
        /// اسکریپت کلیک نهایی را می‌سازد. Nonce، نماد، ISIN، تعداد و قیمت
        /// دوباره تطبیق داده می‌شوند و فقط سپس دکمه رسمی یک‌بار کلیک می‌شود.
        /// درخواست HTTP را خود EasyTrader و نشست رسمی آن ایجاد می‌کنند.
        /// </summary>
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
                    const findOrderContainer = () => {
                        const candidates = Array.from(
                            document.querySelectorAll('[role="dialog"], dialog'))
                            .filter(candidate =>
                                isVisible(candidate) &&
                                candidate.querySelector('#quantity') &&
                                candidate.querySelector('#price'));

                        const quantityInputs = Array.from(
                            document.querySelectorAll('#quantity'))
                            .filter(isVisible);

                        for (const quantityInput of quantityInputs) {
                            let ancestor = quantityInput.parentElement;

                            for (let depth = 0;
                                depth < 12 && ancestor instanceof HTMLElement;
                                depth += 1, ancestor = ancestor.parentElement) {
                                if (ancestor === document.body ||
                                    ancestor === document.documentElement) {
                                    break;
                                }

                                const priceInput =
                                    ancestor.querySelector('#price');
                                const sendButton = Array.from(
                                    ancestor.querySelectorAll('button'))
                                    .find(button =>
                                        isVisible(button) &&
                                        normalizeText(button.textContent) === "ارسال خرید");

                                if (priceInput &&
                                    sendButton &&
                                    !candidates.includes(ancestor)) {
                                    candidates.push(ancestor);
                                }
                            }
                        }

                        const expected = normalizeText(expectedSymbolName);
                        const expectedIsinUpper = expectedSymbolIsin.toUpperCase();

                        return candidates.find(candidate => {
                            const paddedText =
                                " " + normalizeText(candidate.textContent) + " ";
                            const symbolMatches =
                                paddedText.includes(" " + expected + " ");
                            const isinMatches = [candidate, ...candidate.querySelectorAll('*')]
                                .some(element =>
                                    Array.from(element.attributes ?? []).some(attribute =>
                                        String(attribute.value ?? "")
                                            .toUpperCase()
                                            .includes(expectedIsinUpper)));

                            return symbolMatches &&
                                isinMatches;
                        }) || candidates[0] || null;
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

                    const dialog =
                        findOrderContainer();

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

                    // قفل موقت پیش از کلیک حذف می‌شود تا همین آماده‌سازی نتواند
                    // برای کلیک دوم دوباره استفاده شود.
                    delete window[bridgePropertyName];
                    sendButton.click();

                    return result("CLICKED", "Official buy action was invoked once.");
                })()
                """;
        }

        /// <summary>
        /// برای زمان‌بندی سریع، آماده‌سازی و کلیک نهایی را در یک اجرای اتمیک
        /// JavaScript انجام می‌دهد. اگر پنجره سفارش هنوز باز نباشد، فقط درخواست
        /// بازشدن پنجره را صادر می‌کند و هیچ POST سفارشی ایجاد نمی‌شود.
        /// </summary>
        public static string BuildAtomicScheduledSubmitScript(
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

            string prepareScript =
                BuildPrepareScript(
                    order,
                    nonce);

            string ensureDialogScript =
                BuildEnsureBuyDialogScript(
                    order);

            string submitScript =
                BuildSubmitScript(
                    order,
                    nonce);

            return $$"""
                (() => {
                    const prepareResult =
                        {{prepareScript}};

                    if (prepareResult &&
                        prepareResult.status === "PREPARED") {
                        return {{submitScript}};
                    }

                    if (prepareResult &&
                        prepareResult.status === "ORDER_DIALOG_NOT_FOUND") {
                        return {{ensureDialogScript}};
                    }

                    return prepareResult || {
                        status: "INVALID_RESULT",
                        reason: "Atomic scheduled submission returned no result."
                    };
                })()
                """;
        }

        /// <summary>
        /// وضعیت موقت آماده‌سازی را فقط در صورت تطبیق Nonce پاک می‌کند.
        /// این پاک‌سازی به Session، Cookie، Token یا داده‌های EasyTrader دست نمی‌زند.
        /// </summary>
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

        public static OfficialOrderFormReadResult ParseOrderFormReadResult(
            string executeScriptResult)
        {
            if (string.IsNullOrWhiteSpace(executeScriptResult))
            {
                return new OfficialOrderFormReadResult
                {
                    Status = "EMPTY_RESULT",
                    Reason = "EasyTrader returned an empty form-read result."
                };
            }

            try
            {
                return JsonSerializer.Deserialize<OfficialOrderFormReadResult>(
                    executeScriptResult,
                    ResultOptions)
                    ?? new OfficialOrderFormReadResult
                    {
                        Status = "INVALID_RESULT",
                        Reason = "Form-read result could not be parsed."
                    };
            }
            catch (JsonException)
            {
                return new OfficialOrderFormReadResult
                {
                    Status = "INVALID_RESULT",
                    Reason = "Form-read result was not valid JSON."
                };
            }
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
