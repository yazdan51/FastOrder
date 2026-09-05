using FastOrder.ChartTools.Markets;
using FastOrder.ChartTools.Models;

namespace FastOrder.ChartTools.Calculations;

public static class PositionSizingCalculator
{
    public static PositionSizingResult Calculate(
        PositionSizingRequest request,
        SymbolMetadata symbol,
        IMarketNormalizationAdapter normalizationAdapter)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(normalizationAdapter);

        Validate(request);

        var riskAmount = request.RiskMode switch
        {
            RiskInputMode.Absolute => request.RiskValue,
            RiskInputMode.PercentOfAccount => request.RiskValue / 100m * request.AccountSize,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.RiskMode, "Unknown risk input mode.")
        };

        var priceRisk = request.Side switch
        {
            PositionSide.Long => request.EntryPrice - request.StopPrice,
            PositionSide.Short => request.StopPrice - request.EntryPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Side, "Unknown position side.")
        };

        var quantityByRisk =
            (riskAmount / (priceRisk * symbol.PointValue)) /
            symbol.LotSize;

        var quantityByLeverage =
            (request.AccountSize * request.Leverage / request.EntryPrice) *
            symbol.PointValue /
            symbol.LotSize;

        var rawQuantity = Math.Min(quantityByRisk, quantityByLeverage);
        var finalQuantity = normalizationAdapter.NormalizeQuantityDown(rawQuantity, symbol);

        return new PositionSizingResult(
            riskAmount,
            quantityByRisk,
            quantityByLeverage,
            rawQuantity,
            finalQuantity);
    }

    private static void Validate(PositionSizingRequest request)
    {
        if (!Enum.IsDefined(request.Side))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Side, "Unknown position side.");
        }

        if (!Enum.IsDefined(request.RiskMode))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.RiskMode, "Unknown risk input mode.");
        }

        EnsurePositive(request.AccountSize, nameof(request.AccountSize));
        EnsurePositive(request.RiskValue, nameof(request.RiskValue));
        EnsurePositive(request.EntryPrice, nameof(request.EntryPrice));
        EnsurePositive(request.StopPrice, nameof(request.StopPrice));
        EnsurePositive(request.Leverage, nameof(request.Leverage));

        var validStop = request.Side switch
        {
            PositionSide.Long => request.StopPrice < request.EntryPrice,
            PositionSide.Short => request.StopPrice > request.EntryPrice,
            _ => false
        };

        if (!validStop)
        {
            throw new ArgumentException(
                "The stop price must be below entry for long positions and above entry for short positions.",
                nameof(request));
        }
    }

    private static void EnsurePositive(decimal value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be greater than zero.");
        }
    }
}
