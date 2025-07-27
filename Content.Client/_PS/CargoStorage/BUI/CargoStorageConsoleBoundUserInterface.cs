using Content.Client._PS.CargoStorage.UI;
using Content.Shared._PS.CargoStorage.BUI;
using Content.Shared._PS.CargoStorage.Events;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._PS.CargoStorage.BUI;

[UsedImplicitly]
public sealed class CargoStorageConsoleBoundUserInterface : BoundUserInterface
{
    private CargoStorageMenu? _menu;

    public CargoStorageConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        if (_menu == null)
        {
            _menu = this.CreateWindow<CargoStorageMenu>();
            _menu.OnAddToCart1 += args => AddToCart(args, 1);
            _menu.OnAddToCart5 += args => AddToCart(args, 5);
            _menu.OnAddToCart10 += args => AddToCart(args, 10);
            _menu.OnAddToCart30 += args => AddToCart(args, 30);
            _menu.OnAddToCartAll += args => AddToCart(args, int.MaxValue);
            _menu.OnReturn += Return;
            _menu.OnPurchaseCart += PurchaseCrate;
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not CargoStorageConsoleInterfaceState uiState)
            return;

        if (_menu == null)
            return;

        _menu?.SetUiEnabled(uiState.Enabled);
        _menu?.UpdateState(uiState);
    }

    private void AddToCart(BaseButton.ButtonEventArgs args, int amount)
    {
        if (args.Button.Parent?.Parent?.Parent?.Parent is not CargoStorageProductRow product)
            return;
        var addToCartMessage = new CargoStorageCartMessage(amount, product.Prototype.ID);

        SendMessage(addToCartMessage);
    }

    private void Return(BaseButton.ButtonEventArgs args)
    {
        if (args.Button.Parent?.Parent?.Parent is not CargoStorageProductRow product)
            return;
        var purchaseMessage = new CargoStorageCartMessage(0, product.Prototype.ID, true);

        SendMessage(purchaseMessage);
    }

    private void PurchaseCrate(BaseButton.ButtonEventArgs args)
    {
        SendMessage(new CargoStoragePurchaseMessage());
        Close();
    }
}
