using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastOrder
{
    internal sealed class PishroSideStructuralCandidate
    {
        [JsonPropertyName("tagName")]
        public string TagName { get; init; } = "";

        [JsonPropertyName("text")]
        public string Text { get; init; } = "";

        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("class")]
        public string ClassAttribute { get; init; } = "";

        [JsonPropertyName("role")]
        public string Role { get; init; } = "";

        [JsonPropertyName("ariaControls")]
        public string AriaControls { get; init; } = "";

        [JsonPropertyName("ariaSelected")]
        public string AriaSelected { get; init; } = "";

        [JsonPropertyName("ariaDisabled")]
        public string AriaDisabled { get; init; } = "";

        [JsonPropertyName("tabindex")]
        public string TabIndex { get; init; } = "";

        [JsonPropertyName("dataState")]
        public string DataState { get; init; } = "";

        [JsonPropertyName("dataActive")]
        public string DataActive { get; init; } = "";

        [JsonPropertyName("dataSelected")]
        public string DataSelected { get; init; } = "";

        [JsonPropertyName("dataTestId")]
        public string DataTestId { get; init; } = "";

        [JsonPropertyName("dataTest")]
        public string DataTest { get; init; } = "";

        [JsonPropertyName("dataCy")]
        public string DataCy { get; init; } = "";

        [JsonPropertyName("visible")]
        public bool Visible { get; init; }

        [JsonPropertyName("disabled")]
        public bool Disabled { get; init; }
    }

    internal sealed class PishroSideStructuralProbeResult
    {
        [JsonPropertyName("status")]
        public string Status { get; init; } = "";

        [JsonPropertyName("reason")]
        public string Reason { get; init; } = "";

        [JsonPropertyName("candidateCount")]
        public int CandidateCount { get; init; }

        [JsonPropertyName("candidates")]
        public IReadOnlyList<PishroSideStructuralCandidate> Candidates
        {
            get;
            init;
        } = Array.Empty<PishroSideStructuralCandidate>();
    }

    /// <summary>
    /// Adapter مستقل رابط رسمی پیشرو کمان. این Adapter فقط کنترل‌های قابل‌مشاهده
    /// و بدون ابهام را می‌پذیرد، origin و ISIN را تطبیق می‌دهد و هیچ API مستقیمی
    /// فراخوانی نمی‌کند. در هر ابهام DOM، عملیات قبل از کلیک نهایی متوقف می‌شود.
    /// </summary>
    internal static class PishroKamanOrderUiBridge
    {
        private static readonly string[] ExpectedOrigins =
        {
            "https://kaman.pishrobroker.ir",
            "https://mobile.pishrobroker.ir"
        };

        private const string BridgePropertyName =
            "__fastOrderPishroKamanOfficialUiBridgeV1";

        internal const string SideStructuralProbeReadyStatus =
            "PISHRO_SIDE_PROBE_READY";

        private static readonly JsonSerializerOptions ProbeResultOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive =
                    true
            };

        public static string BuildOpenCurrentSymbolBuyDialogScript() =>
            BuildScript("open", null, null);

        public static string BuildReadCurrentOrderFormScript() =>
            BuildScript("read", null, null);

        public static string BuildSideStructuralProbeScript()
        {
            string expectedOrigins =
                JsonSerializer.Serialize(ExpectedOrigins);

            return $$"""
                (() => {
                    const expectedOrigins = {{expectedOrigins}};
                    const result = (status, reason, candidates = []) => ({
                        status,
                        reason,
                        candidateCount: candidates.length,
                        candidates
                    });
                    const normalize = value => String(value ?? "")
                        .replace(/[\s\u200c\u200f\u202a-\u202e]+/g, " ")
                        .trim();
                    const safeAttribute = (element, attributeName) =>
                        normalize(element.getAttribute(attributeName))
                            .slice(0, 240);
                    const visible = element => {
                        if (!(element instanceof HTMLElement)) return false;
                        const style = getComputedStyle(element);
                        return style.display !== "none" &&
                            style.visibility !== "hidden" &&
                            style.opacity !== "0" &&
                            element.getClientRects().length > 0;
                    };
                    const visibleText = element =>
                        normalize(element.innerText || element.textContent)
                            .slice(0, 80);
                    const hasSideControlStructure = element =>
                        element.matches(
                            '[role="tab"],[aria-controls],[aria-selected],' +
                            '[data-state],[data-active],[data-selected]');
                    const sideText = text =>
                        /^(?:خرید|فروش)(?:\s|$)/.test(text);
                    const structurallyDisabled = element =>
                        element.hasAttribute("disabled") ||
                        safeAttribute(element, "aria-disabled")
                            .toLowerCase() === "true" ||
                        element.matches(":disabled");

                    if (!expectedOrigins.includes(location.origin))
                        return result(
                            "INVALID_ORIGIN",
                            "The active page is not a trusted Pishro origin.");

                    const candidates = Array.from(document.querySelectorAll(
                        'button,[role="button"],[role="tab"],a,div,span,' +
                        '[tabindex],[aria-controls],[aria-selected],' +
                        '[data-state],[data-active],[data-selected]'))
                        .filter(visible)
                        .filter(element => {
                            const text = visibleText(element);
                            return hasSideControlStructure(element) ||
                                sideText(text);
                        })
                        .map(element => ({
                            tagName: normalize(element.tagName).slice(0, 40),
                            text: visibleText(element),
                            id: safeAttribute(element, "id"),
                            class: safeAttribute(element, "class"),
                            role: safeAttribute(element, "role"),
                            ariaControls: safeAttribute(
                                element,
                                "aria-controls"),
                            ariaSelected: safeAttribute(
                                element,
                                "aria-selected"),
                            ariaDisabled: safeAttribute(
                                element,
                                "aria-disabled"),
                            tabindex: safeAttribute(element, "tabindex"),
                            dataState: safeAttribute(element, "data-state"),
                            dataActive: safeAttribute(element, "data-active"),
                            dataSelected: safeAttribute(
                                element,
                                "data-selected"),
                            dataTestId: safeAttribute(
                                element,
                                "data-testid"),
                            dataTest: safeAttribute(element, "data-test"),
                            dataCy: safeAttribute(element, "data-cy"),
                            visible: true,
                            disabled: structurallyDisabled(element)
                        }));

                    return result(
                        "{{SideStructuralProbeReadyStatus}}",
                        "Visible Pishro side-control candidates were described without interaction.",
                        candidates);
                })()
                """;
        }

        public static PishroSideStructuralProbeResult
            ParseSideStructuralProbeResult(
                string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new PishroSideStructuralProbeResult
                {
                    Status = "EMPTY_RESULT",
                    Reason = "Pishro side structural probe returned no result."
                };
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(json);

                string normalizedJson =
                    document.RootElement.ValueKind ==
                    JsonValueKind.String
                        ? document.RootElement.GetString() ?? ""
                        : document.RootElement.GetRawText();

                return JsonSerializer
                    .Deserialize<PishroSideStructuralProbeResult>(
                        normalizedJson,
                        ProbeResultOptions) ??
                    new PishroSideStructuralProbeResult
                    {
                        Status = "INVALID_RESULT",
                        Reason = "Pishro side structural probe result was invalid."
                    };
            }
            catch (JsonException)
            {
                return new PishroSideStructuralProbeResult
                {
                    Status = "INVALID_RESULT",
                    Reason = "Pishro side structural probe result could not be parsed."
                };
            }
        }

        public static string BuildClickCurrentOfficialOrderButtonScript(
            ScheduledClickSide side)
        {
            if (side is not
                (ScheduledClickSide.Buy or ScheduledClickSide.Sell))
            {
                throw new ArgumentOutOfRangeException(nameof(side));
            }

            string expectedOrigins =
                JsonSerializer.Serialize(ExpectedOrigins);

            string sideClassToken =
                JsonSerializer.Serialize(
                    side == ScheduledClickSide.Buy
                        ? "buy"
                        : "sale");

            string sideLabel =
                JsonSerializer.Serialize(
                    side == ScheduledClickSide.Buy
                        ? "خرید"
                        : "فروش");

            string sideName =
                JsonSerializer.Serialize(
                    side == ScheduledClickSide.Buy
                        ? "BUY"
                        : "SELL");

            return $$"""
                (() => {
                    const result = (status, reason) => ({ status, reason });
                    const expectedOrigins = {{expectedOrigins}};
                    const sideClassToken = {{sideClassToken}};
                    const sideLabel = {{sideLabel}};
                    const sideName = {{sideName}};
                    const norm = value => String(value ?? "")
                        .replace(/[\s\u200c\u200f\u202a-\u202e]+/g, " ")
                        .trim();
                    const visible = element => {
                        if (!(element instanceof HTMLElement)) return false;
                        const style = getComputedStyle(element);
                        return style.display !== "none" &&
                            style.visibility !== "hidden" &&
                            style.opacity !== "0" &&
                            element.getClientRects().length > 0;
                    };

                    if (!expectedOrigins.includes(location.origin))
                        return result("INVALID_ORIGIN",
                            "Official Pishro Kaman origin was not active.");

                    const actions = Array.from(document.querySelectorAll(
                        "button,[role=button],input[type=submit]"))
                        .filter(visible)
                        .filter(action => {
                            const label = norm(
                                action.textContent ||
                                action.getAttribute("value") ||
                                action.getAttribute("aria-label"));
                            const classTokens = String(
                                action.getAttribute("class") || "")
                                .split(/\s+/)
                                .filter(Boolean);

                            return classTokens.includes(sideClassToken) &&
                                (label === sideLabel ||
                                 label.startsWith(sideLabel + " "));
                        });

                    if (actions.length === 0)
                        return result("ORDER_ACTION_NOT_FOUND",
                            "One visible official Pishro " + sideName +
                            " action was not found.");

                    if (actions.length !== 1)
                        return result("ORDER_ACTION_AMBIGUOUS",
                            "More than one visible official Pishro " +
                            sideName + " action was found.");

                    const action = actions[0];
                    if (!(action instanceof HTMLElement))
                        return result("ORDER_ACTION_NOT_FOUND",
                            "Official Pishro " + sideName +
                            " action was not usable.");

                    if (((action instanceof HTMLButtonElement ||
                          action instanceof HTMLInputElement) && action.disabled) ||
                        action.hasAttribute("disabled") ||
                        action.getAttribute("aria-disabled") === "true")
                        return result("ORDER_ACTION_DISABLED",
                            "Official Pishro " + sideName +
                            " action was disabled.");

                    action.click();
                    return result("CLICKED",
                        "Official Pishro " + sideName +
                        " action was invoked once.");
                })()
                """;
        }

        public static string BuildEnsureBuyDialogScript(
            Order order)
        {
            ArgumentNullException.ThrowIfNull(order);
            return BuildScript("ensure", order, null);
        }

        public static string BuildPrepareScript(
            Order order,
            string nonce)
        {
            ValidateOrderAndNonce(order, nonce);
            return BuildScript("prepare", order, nonce);
        }

        public static string BuildSubmitScript(
            Order order,
            string nonce)
        {
            ValidateOrderAndNonce(order, nonce);
            return BuildScript("submit", order, nonce);
        }

        public static string BuildAtomicScheduledSubmitScript(
            Order order,
            string nonce)
        {
            ValidateOrderAndNonce(order, nonce);
            return BuildScript("atomic", order, nonce);
        }

        public static string BuildClearScript(
            string nonce)
        {
            if (string.IsNullOrWhiteSpace(nonce))
            {
                throw new ArgumentException(
                    "Submission nonce cannot be empty.",
                    nameof(nonce));
            }

            return BuildScript("clear", null, nonce);
        }

        private static void ValidateOrderAndNonce(
            Order order,
            string nonce)
        {
            ArgumentNullException.ThrowIfNull(order);

            if (string.IsNullOrWhiteSpace(nonce))
            {
                throw new ArgumentException(
                    "Submission nonce cannot be empty.",
                    nameof(nonce));
            }
        }

        private static string BuildScript(
            string mode,
            Order? order,
            string? nonce)
        {
            string expectedOrigins =
                JsonSerializer.Serialize(ExpectedOrigins);

            string serializedMode =
                JsonSerializer.Serialize(mode);

            string expectedSymbolName =
                JsonSerializer.Serialize(order?.SymbolName ?? "");

            string expectedSymbolIsin =
                JsonSerializer.Serialize(order?.SymbolIsin ?? "");

            string expectedNonce =
                JsonSerializer.Serialize(nonce ?? "");

            string bridgePropertyName =
                JsonSerializer.Serialize(BridgePropertyName);

            string expectedQuantity =
                order?.Quantity.ToString(CultureInfo.InvariantCulture) ?? "";

            string expectedPrice =
                order?.Price.ToString(CultureInfo.InvariantCulture) ?? "";

            return $$"""
                (() => {
                    const result = (status, reason, symbolName="", symbolIsin="",
                                    price="", quantity="", side=0,
                                    commissionAmount="", totalValue="") =>
                        ({status, reason, symbolName, symbolIsin, price, quantity, side,
                          commissionAmount, totalValue});
                    const mode = {{serializedMode}};
                    const expectedOrigins = {{expectedOrigins}};
                    const expectedSymbolName = {{expectedSymbolName}};
                    const expectedSymbolIsin = {{expectedSymbolIsin}}.toUpperCase();
                    const expectedQuantity = "{{expectedQuantity}}";
                    const expectedPrice = "{{expectedPrice}}";
                    const expectedNonce = {{expectedNonce}};
                    const bridgePropertyName = {{bridgePropertyName}};
                    const norm = value => String(value ?? "")
                        .replace(/[\s\u200c\u200f\u202a-\u202e]+/g, " ")
                        .trim();
                    const num = value => String(value ?? "")
                        .replace(/[۰-۹]/g, character =>
                            String(character.charCodeAt(0) - "۰".charCodeAt(0)))
                        .replace(/[٠-٩]/g, character =>
                            String(character.charCodeAt(0) - "٠".charCodeAt(0)))
                        .replace(/[^0-9]/g, "");
                    const visible = element => {
                        if (!(element instanceof HTMLElement)) return false;
                        const style = getComputedStyle(element);
                        return style.display !== "none" &&
                            style.visibility !== "hidden" &&
                            style.opacity !== "0" &&
                            element.getClientRects().length > 0;
                    };
                    const inputDescriptor = input => {
                        const parts = [
                            input.id,
                            input.name,
                            input.placeholder,
                            input.getAttribute("aria-label"),
                            input.getAttribute("data-testid"),
                            input.getAttribute("data-test"),
                            input.getAttribute("data-cy")
                        ];
                        if (input.id && typeof CSS !== "undefined" && CSS.escape) {
                            const label = document.querySelector(
                                `label[for="${CSS.escape(input.id)}"]`);
                            if (label) parts.push(label.textContent);
                        }
                        const wrappingLabel = input.closest("label");
                        if (wrappingLabel) parts.push(wrappingLabel.textContent);
                        let parent = input.parentElement;
                        for (let depth = 0;
                             depth < 3 && parent instanceof HTMLElement;
                             depth += 1, parent = parent.parentElement) {
                            const text = norm(parent.textContent);
                            if (text.length <= 80) parts.push(text);
                        }
                        return norm(parts.filter(Boolean).join(" "));
                    };
                    const usableInputs = () => Array.from(
                        document.querySelectorAll("input"))
                        .filter(input => input instanceof HTMLInputElement &&
                            visible(input) &&
                            !["hidden","button","submit","checkbox","radio"]
                                .includes(String(input.type || "text").toLowerCase()));
                    const chooseInput = kind => {
                        const wanted = kind === "price"
                            ? /(قیمت|price)/i
                            : /(تعداد|حجم|quantity|volume)/i;
                        const unwanted = kind === "price"
                            ? /(تعداد|حجم|quantity|volume)/i
                            : /(قیمت|price)/i;
                        const ranked = usableInputs()
                            .map(input => {
                                const descriptor = inputDescriptor(input);
                                let score = wanted.test(descriptor) ? 100 : 0;
                                if (unwanted.test(descriptor)) score -= 40;
                                return { input, descriptor, score };
                            })
                            .filter(item => item.score > 0)
                            .sort((left, right) => right.score - left.score);
                        if (ranked.length === 0) return null;
                        if (ranked.length > 1 && ranked[0].score === ranked[1].score)
                            return null;
                        return ranked[0].input;
                    };
                    const commonScope = (first, second) => {
                        if (!(first instanceof HTMLElement) ||
                            !(second instanceof HTMLElement)) return null;
                        let ancestor = first;
                        for (let depth = 0;
                             depth < 18 && ancestor instanceof HTMLElement;
                             depth += 1, ancestor = ancestor.parentElement) {
                            if (ancestor.contains(second) && visible(ancestor))
                                return ancestor;
                            if (ancestor === document.body) break;
                        }
                        return null;
                    };
                    const findBuyAction = scope => {
                        if (!(scope instanceof Element)) return null;
                        const actions = Array.from(
                            scope.querySelectorAll("button,[role=button],input[type=submit]"))
                            .filter(visible)
                            .filter(action => {
                                const text = norm(
                                    action.textContent ||
                                    action.getAttribute("value") ||
                                    action.getAttribute("aria-label"));
                                const classTokens = String(
                                    action.getAttribute("class") || "")
                                    .split(/\s+/)
                                    .filter(Boolean);

                                // Runtime-validated Kaman contract:
                                // official buy action has class token "buy" and a visible
                                // label beginning with "خرید" (for example "خرید جوانه کوچک").
                                return classTokens.includes("buy") &&
                                    /^خرید(?:\s|$)/.test(text);
                            });
                        return actions.length === 1 ? actions[0] : null;
                    };
                    const findSafeBuyTab = () => {
                        const tabs = Array.from(
                            document.querySelectorAll('[role="tab"],button[aria-controls]'))
                            .filter(visible)
                            .filter(tab => norm(tab.textContent) === "خرید" &&
                                tab.getAttribute("aria-disabled") !== "true");
                        return tabs.length === 1 ? tabs[0] : null;
                    };
                    const locateForm = () => {
                        // Kaman renders BUY and SELL forms at the same time and reuses
                        // price-input / count-input IDs. Therefore the inputs must be
                        // resolved inside the scope of the unique visible BUY action,
                        // never globally.
                        const allBuyActions = Array.from(
                            document.querySelectorAll(
                                "button,[role=button],input[type=submit]"))
                            .filter(visible)
                            .filter(action => {
                                const text = norm(
                                    action.textContent ||
                                    action.getAttribute("value") ||
                                    action.getAttribute("aria-label"));
                                const classTokens = String(
                                    action.getAttribute("class") || "")
                                    .split(/\s+/)
                                    .filter(Boolean);
                                return classTokens.includes("buy") &&
                                    /^خرید(?:\s|$)/.test(text);
                            });

                        if (allBuyActions.length !== 1)
                            return null;

                        const buyAction = allBuyActions[0];
                        let scope = buyAction.parentElement;

                        for (let depth = 0;
                             depth < 16 && scope instanceof HTMLElement;
                             depth += 1, scope = scope.parentElement) {
                            if (!visible(scope)) continue;

                            const priceInputs = Array.from(
                                scope.querySelectorAll('input#price-input'))
                                .filter(input =>
                                    input instanceof HTMLInputElement &&
                                    visible(input));

                            const quantityInputs = Array.from(
                                scope.querySelectorAll('input#count-input'))
                                .filter(input =>
                                    input instanceof HTMLInputElement &&
                                    visible(input));

                            if (priceInputs.length === 1 &&
                                quantityInputs.length === 1) {
                                return {
                                    scope,
                                    priceInput: priceInputs[0],
                                    quantityInput: quantityInputs[0],
                                    buyAction
                                };
                            }

                            if (scope === document.body)
                                break;
                        }

                        return null;
                    };
                    const extractIsins = value => Array.from(new Set(
                        String(value ?? "")
                            .toUpperCase()
                            .match(/IR[A-Z0-9]{10}/g) || []));
                    const isinsFromElements = elements => {
                        const found = new Set();
                        for (const element of elements) {
                            if (!(element instanceof Element)) continue;
                            for (const attribute of Array.from(element.attributes || [])) {
                                for (const isin of extractIsins(attribute.value))
                                    found.add(isin);
                            }
                        }
                        return Array.from(found);
                    };
                    const discoverActiveInstrument = formScope => {
                        const urlIsins = extractIsins(location.href);
                        if (urlIsins.length > 0)
                            return { candidates: urlIsins, source: "url" };

                        if (formScope instanceof Element) {
                            const formElements = [
                                formScope,
                                ...formScope.querySelectorAll("*")
                            ];
                            let ancestor = formScope.parentElement;
                            for (let depth = 0;
                                 depth < 5 && ancestor instanceof HTMLElement;
                                 depth += 1, ancestor = ancestor.parentElement) {
                                formElements.push(ancestor);
                            }
                            const formIsins = isinsFromElements(formElements);
                            if (formIsins.length > 0)
                                return { candidates: formIsins, source: "order-form" };

                            const visibleFormText = norm(formScope.textContent);
                            if (visibleFormText.length <= 2000) {
                                const textIsins = extractIsins(visibleFormText);
                                if (textIsins.length > 0)
                                    return { candidates: textIsins, source: "visible-form" };
                            }
                        }

                        const selectedRoots = Array.from(document.querySelectorAll(
                            '[aria-selected="true"],' +
                            '[data-selected="true"],' +
                            '[data-active="true"],' +
                            '[data-state="active"]'))
                            .filter(visible);
                        const selectedElements = [];
                        for (const root of selectedRoots) {
                            selectedElements.push(root);
                            selectedElements.push(...root.querySelectorAll("*"));
                        }
                        const selectedIsins = isinsFromElements(selectedElements);
                        if (selectedIsins.length > 0)
                            return { candidates: selectedIsins, source: "active-selection" };

                        return { candidates: [], source: "none" };
                    };
                    const buySymbolName = form => {
                        if (!form || !(form.buyAction instanceof HTMLElement))
                            return "";
                        const text = norm(
                            form.buyAction.textContent ||
                            form.buyAction.getAttribute("value") ||
                            form.buyAction.getAttribute("aria-label"));
                        return norm(text.replace(/^خرید(?:\s+|$)/, ""));
                    };
                    const verifyBuySymbolName = form => {
                        const currentSymbolName = buySymbolName(form);
                        if (!currentSymbolName)
                            return result("INSTRUMENT_NOT_VERIFIED",
                                "Visible Pishro buy action did not expose a symbol name.");
                        if (expectedSymbolName &&
                            norm(currentSymbolName) !== norm(expectedSymbolName))
                            return result("INSTRUMENT_NOT_VERIFIED",
                                "Visible Pishro buy symbol name did not match the confirmed order.");
                        return null;
                    };
                    const pageSymbolName = isin => {
                        const selectors = [
                            "[data-symbol-name]",
                            "[data-instrument-name]",
                            "[data-testid*=symbol]",
                            "[class*=symbol-name]",
                            "[class*=instrument-name]"
                        ];
                        for (const selector of selectors) {
                            for (const element of document.querySelectorAll(selector)) {
                                if (!visible(element)) continue;
                                const text = norm(
                                    element.getAttribute("data-symbol-name") ||
                                    element.getAttribute("data-instrument-name") ||
                                    element.textContent);
                                if (text && text.length <= 100 &&
                                    !/^[0-9,._\s]+$/.test(text)) return text;
                            }
                        }
                        return isin;
                    };
                    const labeledNumber = (scope, labels) => {
                        if (!(scope instanceof Element)) return "";
                        const elements = Array.from(scope.querySelectorAll("*"))
                            .filter(visible);
                        for (const label of elements) {
                            const labelText = norm(label.textContent)
                                .replace(/[:：]/g, "");
                            if (!labels.includes(labelText)) continue;
                            const candidates = [
                                label.nextElementSibling,
                                label.previousElementSibling,
                                label.parentElement?.nextElementSibling
                            ].filter(Boolean);
                            for (const candidate of candidates) {
                                const value = num(candidate.textContent);
                                if (value && Number(value) > 0) return value;
                            }
                            const parentText = norm(label.parentElement?.textContent || "");
                            const value = num(parentText.replace(label.textContent || "", ""));
                            if (value && Number(value) > 0) return value;
                        }
                        return "";
                    };
                    const setInputValue = (input, value) => {
                        const setter = Object.getOwnPropertyDescriptor(
                            HTMLInputElement.prototype, "value")?.set;
                        if (typeof setter !== "function") return false;
                        setter.call(input, value);
                        input.dispatchEvent(new Event("input", { bubbles: true }));
                        input.dispatchEvent(new Event("change", { bubbles: true }));
                        input.dispatchEvent(new Event("blur", { bubbles: true }));
                        return num(input.value) === value;
                    };
                    const verifyInstrument = discovery => {
                        const candidates = Array.isArray(discovery?.candidates)
                            ? discovery.candidates
                            : [];
                        if (candidates.length === 0)
                            return result("INSTRUMENT_NOT_VERIFIED",
                                "Pishro URL, visible order form, and active selection did not expose a valid ISIN.");
                        if (candidates.length !== 1)
                            return result("INSTRUMENT_AMBIGUOUS",
                                `Pishro exposed ${candidates.length} possible ISIN values in ${discovery.source}.`);
                        const currentIsin = candidates[0];
                        if (expectedSymbolIsin && currentIsin !== expectedSymbolIsin)
                            return result("INSTRUMENT_NOT_VERIFIED",
                                `Pishro ${discovery.source} ISIN did not match the confirmed order.`);
                        return null;
                    };
                    const prepare = () => {
                        const form = locateForm();
                        if (!form)
                            return result("ORDER_DIALOG_NOT_FOUND",
                                "One unambiguous visible Pishro buy form was not found.");
                        const instrumentError = verifyBuySymbolName(form);
                        if (instrumentError) return instrumentError;
                        if (form.buyAction instanceof HTMLButtonElement &&
                            (form.buyAction.disabled ||
                             form.buyAction.getAttribute("aria-disabled") === "true"))
                            return result("ORDER_ACTION_DISABLED",
                                "Official Pishro buy action was disabled.");
                        if (!setInputValue(form.quantityInput, expectedQuantity) ||
                            !setInputValue(form.priceInput, expectedPrice))
                            return result("INPUT_UPDATE_FAILED",
                                "Pishro order values did not remain set.");
                        window[bridgePropertyName] = Object.freeze({
                            nonce: expectedNonce,
                            symbolName: expectedSymbolName,
                            symbolIsin: expectedSymbolIsin,
                            quantity: expectedQuantity,
                            price: expectedPrice
                        });
                        return result("PREPARED",
                            "Official Pishro order form is ready for final confirmation.");
                    };
                    const submit = () => {
                        const prepared = window[bridgePropertyName];
                        if (!prepared ||
                            prepared.nonce !== expectedNonce ||
                            prepared.symbolName !== expectedSymbolName ||
                            prepared.symbolIsin !== expectedSymbolIsin ||
                            prepared.quantity !== expectedQuantity ||
                            prepared.price !== expectedPrice)
                            return result("PREPARATION_EXPIRED",
                                "Prepared Pishro form state was no longer valid.");
                        const form = locateForm();
                        if (!form)
                            return result("ORDER_DIALOG_NOT_FOUND",
                                "One unambiguous visible Pishro buy form was not found.");
                        const instrumentError = verifyBuySymbolName(form);
                        if (instrumentError) return instrumentError;
                        if (num(form.quantityInput.value) !== expectedQuantity ||
                            num(form.priceInput.value) !== expectedPrice)
                            return result("ORDER_VALUES_CHANGED",
                                "Official Pishro order values changed after preparation.");
                        if (form.buyAction instanceof HTMLButtonElement &&
                            (form.buyAction.disabled ||
                             form.buyAction.getAttribute("aria-disabled") === "true"))
                            return result("ORDER_ACTION_DISABLED",
                                "Official Pishro buy action was disabled.");
                        delete window[bridgePropertyName];
                        form.buyAction.click();
                        return result("CLICKED",
                            "Official Pishro buy action was invoked once.");
                    };

                    if (!expectedOrigins.includes(location.origin))
                        return result("INVALID_ORIGIN",
                            "Official Pishro Kaman origin was not active.");

                    if (mode === "clear") {
                        const prepared = window[bridgePropertyName];
                        if (prepared && prepared.nonce === expectedNonce)
                            delete window[bridgePropertyName];
                        return true;
                    }

                    if (mode === "open" || mode === "ensure") {
                        const form = locateForm();
                        if (mode === "ensure") {
                            if (!form)
                                return result("ORDER_DIALOG_NOT_FOUND",
                                    "One unambiguous visible Pishro buy form was not found.");
                            const instrumentError = verifyBuySymbolName(form);
                            if (instrumentError) return instrumentError;
                        }
                        if (form)
                            return result("DIALOG_ALREADY_OPEN",
                                "Usable official Pishro buy form is already visible.");
                        const buyTab = findSafeBuyTab();
                        if (buyTab) {
                            buyTab.click();
                            return result("DIALOG_OPEN_REQUESTED",
                                "Official Pishro buy tab was selected once.");
                        }
                        return result("ORDER_DIALOG_NOT_FOUND",
                            "Open the official Pishro buy form for the current symbol.");
                    }

                    if (mode === "read") {
                        const form = locateForm();
                        if (!form)
                            return result("ORDER_DIALOG_NOT_FOUND",
                                "One unambiguous visible Pishro buy form was not found.");
                        const instrumentError = verifyBuySymbolName(form);
                        if (instrumentError) return instrumentError;
                        const currentSymbolName = buySymbolName(form);
                        const readInputNumber = input => {
                            if (!(input instanceof HTMLInputElement)) return "";
                            const candidates = [
                                input.value,
                                input.getAttribute("value"),
                                input.getAttribute("data-value"),
                                input.getAttribute("aria-valuenow")
                            ];
                            for (const candidate of candidates) {
                                const normalized = num(candidate);
                                if (normalized) return normalized;
                            }
                            return "";
                        };
                        const price = readInputNumber(form.priceInput);
                        const quantity = readInputNumber(form.quantityInput);
                        if (!price || !quantity)
                            return result(
                                "ORDER_VALUES_NOT_READY",
                                "Visible Pishro buy form was found, but price and/or quantity was empty.",
                                currentSymbolName,
                                "",
                                price,
                                quantity,
                                0,
                                "",
                                "");
                        return result(
                            "FORM_READ",
                            "Visible official Pishro buy form was read.",
                            currentSymbolName,
                            "",
                            price,
                            quantity,
                            0,
                            "",
                            "");
                    }

                    if (mode === "prepare") return prepare();
                    if (mode === "submit") return submit();
                    if (mode === "atomic") {
                        const preparedResult = prepare();
                        return preparedResult && preparedResult.status === "PREPARED"
                            ? submit()
                            : preparedResult;
                    }

                    return result("INVALID_OPERATION",
                        "Unsupported Pishro bridge operation.");
                })()
                """;
        }
    }
}
