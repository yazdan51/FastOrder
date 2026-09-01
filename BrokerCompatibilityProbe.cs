using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastOrder
{
    internal sealed class BrokerCompatibilityControl
    {
        [JsonPropertyName("tag")]
        public string Tag { get; init; } = "";

        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("role")]
        public string Role { get; init; } = "";

        [JsonPropertyName("placeholder")]
        public string Placeholder { get; init; } = "";

        [JsonPropertyName("ariaLabel")]
        public string AriaLabel { get; init; } = "";

        [JsonPropertyName("title")]
        public string Title { get; init; } = "";

        [JsonPropertyName("testId")]
        public string TestId { get; init; } = "";

        [JsonPropertyName("className")]
        public string ClassName { get; init; } = "";

        [JsonPropertyName("labelText")]
        public string LabelText { get; init; } = "";

        [JsonPropertyName("text")]
        public string Text { get; init; } = "";
    }

    internal sealed class BrokerCompatibilityProbeResult
    {
        [JsonPropertyName("status")]
        public string Status { get; init; } = "";

        [JsonPropertyName("reason")]
        public string Reason { get; init; } = "";

        [JsonPropertyName("origin")]
        public string Origin { get; init; } = "";

        [JsonPropertyName("path")]
        public string Path { get; init; } = "";

        [JsonPropertyName("visibleDialogCount")]
        public int VisibleDialogCount { get; init; }

        [JsonPropertyName("inputs")]
        public IReadOnlyList<BrokerCompatibilityControl> Inputs
        {
            get;
            init;
        } = Array.Empty<BrokerCompatibilityControl>();

        [JsonPropertyName("actions")]
        public IReadOnlyList<BrokerCompatibilityControl> Actions
        {
            get;
            init;
        } = Array.Empty<BrokerCompatibilityControl>();
    }

    internal static class BrokerCompatibilityProbe
    {
        public const string ReadyStatus =
            "PROBE_READY";

        private static readonly JsonSerializerOptions ResultOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive =
                    true
            };

        public static string BuildScript(
            string expectedOrigin)
        {
            string serializedOrigin =
                JsonSerializer.Serialize(
                    expectedOrigin);

            return $$"""
                (() => {
                    const expectedOrigin = {{serializedOrigin}};
                    const visible = element => {
                        if (!(element instanceof HTMLElement)) return false;
                        const style = getComputedStyle(element);
                        return style.display !== "none" &&
                            style.visibility !== "hidden" &&
                            style.opacity !== "0" &&
                            element.getClientRects().length > 0;
                    };
                    const clean = value => String(value ?? "")
                        .replace(/[\r\n\t]+/g, " ")
                        .replace(/\s+/g, " ")
                        .trim()
                        .slice(0, 160)
                        .replace(/[0-9۰-۹]{4,}/g, "#");
                    const safeActionText = value => clean(value)
                        .replace(/[0-9۰-۹]/g, "#");
                    const describe = element => {
                        const ownLabel = element.closest("label");
                        const externalLabel = element.id
                            ? Array.from(document.querySelectorAll("label"))
                                .find(label => label.htmlFor === element.id)
                            : null;

                        return {
                            tag: clean(element.tagName).toLowerCase(),
                            id: clean(element.id),
                            name: clean(element.getAttribute("name")),
                            type: clean(element.getAttribute("type")),
                            role: clean(element.getAttribute("role")),
                            placeholder: clean(element.getAttribute("placeholder")),
                            ariaLabel: clean(element.getAttribute("aria-label")),
                            title: clean(element.getAttribute("title")),
                            testId: clean(
                                element.getAttribute("data-testid") ||
                                element.getAttribute("data-test") ||
                                element.getAttribute("data-cy")),
                            className: clean(element.getAttribute("class")),
                            labelText: safeActionText(
                                ownLabel?.textContent ||
                                externalLabel?.textContent),
                            text: element.matches("button,[role=button]")
                                ? safeActionText(element.textContent)
                                : ""
                        };
                    };

                    if (location.origin !== expectedOrigin) {
                        return {
                            status: "INVALID_ORIGIN",
                            reason: "The selected broker origin is not active.",
                            origin: location.origin,
                            path: location.pathname,
                            visibleDialogCount: 0,
                            inputs: [],
                            actions: []
                        };
                    }

                    const inputs = Array.from(
                        document.querySelectorAll("input,select,textarea"))
                        .filter(visible)
                        .slice(0, 40)
                        .map(describe);
                    const actions = Array.from(
                        document.querySelectorAll("button,[role=button]"))
                        .filter(visible)
                        .slice(0, 60)
                        .map(describe);
                    const visibleDialogCount = Array.from(
                        document.querySelectorAll('[role="dialog"],dialog'))
                        .filter(visible)
                        .length;

                    return {
                        status: "PROBE_READY",
                        reason: "Visible structural attributes collected without field values.",
                        origin: location.origin,
                        path: location.pathname,
                        visibleDialogCount,
                        inputs,
                        actions
                    };
                })()
                """;
        }

        public static BrokerCompatibilityProbeResult ParseResult(
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new BrokerCompatibilityProbeResult
                {
                    Status =
                        "EMPTY_RESULT",

                    Reason =
                        "Broker compatibility probe returned no result."
                };
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(
                        json);

                string normalizedJson =
                    document.RootElement.ValueKind ==
                    JsonValueKind.String
                        ? document.RootElement.GetString() ?? ""
                        : document.RootElement.GetRawText();

                return JsonSerializer.Deserialize<BrokerCompatibilityProbeResult>(
                    normalizedJson,
                    ResultOptions) ??
                    new BrokerCompatibilityProbeResult
                    {
                        Status =
                            "INVALID_RESULT",

                        Reason =
                            "Broker compatibility probe result was invalid."
                    };
            }
            catch (JsonException)
            {
                return new BrokerCompatibilityProbeResult
                {
                    Status =
                        "INVALID_RESULT",

                    Reason =
                        "Broker compatibility probe result could not be parsed."
                };
            }
        }
    }
}
