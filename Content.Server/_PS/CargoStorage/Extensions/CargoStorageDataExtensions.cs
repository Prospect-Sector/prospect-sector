using Content.Shared._PS.CargoStorage.Data;

namespace Content.Server._PS.CargoStorage.Extensions;

using System.Linq;
using Robust.Shared.Prototypes;

public static class CargoStorageDataExtensions
{
    /// <summary>
    /// Update-or-insert the cargo storage data list or adds it new if it doesnt exist in there yet.
    /// </summary>
    /// <param name="entityPrototypeId">The entity prototype id to change the amount of.</param>
    /// <param name="increaseAmount">The change in units, ie. 6 plushies.</param>
    /// <param name="marketDataList">The cargo storage data list to modify.</param>
    /// <param name="stackPrototypeId">The stack prototype id for this prototype if any.</param>
    /// <remarks>
    /// For existing data, increaseAmount is not validated. Any update that would result in a non-positive quantity results in item removal.
    /// </remarks>
    public static void Upsert(this List<CargoStorageData> marketDataList,
        string entityPrototypeId,
        int increaseAmount,
        string? stackPrototypeId = null)
    {
        // Find the MarketData for the given EntityPrototype.
        var prototypeMarketData = marketDataList.FirstOrDefault(md => md.Prototype == entityPrototypeId);

        if (prototypeMarketData != null)
        {
            prototypeMarketData.Quantity += increaseAmount;

            // Prune empty/negative quantities (overflow, emptying, or excessive withdrawal)
            if (prototypeMarketData.Quantity <= 0)
                marketDataList.Remove(prototypeMarketData);
        }
        else if (increaseAmount > 0)
        {
            // If it doesn't exist, create a new MarketData and add it to the list.
            marketDataList.Add(new CargoStorageData(entityPrototypeId,
                stackPrototypeId ?? prototypeMarketData?.StackPrototype,
                increaseAmount));
        }
    }

    /// <summary>
    /// Moves a MarketData item from the source list to the target list.
    /// </summary>
    /// <param name="sourceList">The source list to move the item from.</param>
    /// <param name="targetList">The target list to move the item to.</param>
    /// <param name="prototypeId">The prototype ID of the item to move.</param>
    public static void Move(this List<CargoStorageData> sourceList, List<CargoStorageData> targetList, string prototypeId)
    {
        var marketData = sourceList.FirstOrDefault(md => md.Prototype == prototypeId);
        if (marketData != null)
        {
            targetList.Upsert(marketData.Prototype, marketData.Quantity, marketData.StackPrototype);
            sourceList.Remove(marketData);
        }
    }

    /// <summary>
    /// Get the current maximum amount available for a particular prototype.
    /// </summary>
    /// <param name="marketDataList">the list to check in</param>
    /// <param name="prototype">the prototype to check for</param>
    /// <returns>The max quantity withdrawable</returns>
    public static int GetMaxQuantityToWithdraw(this List<CargoStorageData> marketDataList, EntityPrototype prototype)
    {
        var marketData = marketDataList.FirstOrDefault(md => md.Prototype == prototype.ID);
        return marketData == null ? 0 : marketData.Quantity;
    }

}
