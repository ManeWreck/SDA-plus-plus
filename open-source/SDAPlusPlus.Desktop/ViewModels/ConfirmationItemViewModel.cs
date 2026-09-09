using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using SDAPlusPlus.Core.Confirmations;
using SDAPlusPlus.Desktop.Services;
using SteamAuth;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class ConfirmationItemViewModel : ViewModelBase
{
    private bool _isRussian;

    public ConfirmationItemViewModel(AccountConfirmation confirmation, bool isRussian)
    {
        Confirmation = confirmation;
        _isRussian = isRussian;
    }

    public AccountConfirmation Confirmation { get; }
    public string AccountName => Confirmation.AccountName;
    public string SteamId => Confirmation.SteamId;
    public string Initial => string.IsNullOrWhiteSpace(AccountName) ? "?" : AccountName[..1].ToUpperInvariant();
    public string SenderInitial => string.IsNullOrWhiteSpace(Headline) ? "?" : Headline[..1].ToUpperInvariant();
    public bool HasSenderAvatar => SenderAvatar is not null;
    public bool HasReceiverAvatar => ReceiverAvatar is not null;
    public bool ShowSenderFallback => !HasSenderAvatar;
    public bool ShowReceiverFallback => !HasReceiverAvatar;
    public bool IsTrade => Confirmation.Type == SteamAuth.Confirmation.EMobileConfirmationType.Trade;
    public string Headline => string.IsNullOrWhiteSpace(Confirmation.Headline)
        ? (_isRussian ? "Подтверждение Steam" : "Steam confirmation")
        : Confirmation.Headline;
    public string SummaryText => string.Join(Environment.NewLine, Confirmation.Summary);
    public string TypeLabel => Confirmation.Type switch
    {
        SteamAuth.Confirmation.EMobileConfirmationType.Trade => _isRussian ? "Обмен" : "Trade",
        SteamAuth.Confirmation.EMobileConfirmationType.MarketListing => _isRussian ? "Торговая площадка" : "Market listing",
        SteamAuth.Confirmation.EMobileConfirmationType.PhoneNumberChange => _isRussian ? "Смена телефона" : "Phone change",
        SteamAuth.Confirmation.EMobileConfirmationType.AccountRecovery => _isRussian ? "Восстановление аккаунта" : "Account recovery",
        SteamAuth.Confirmation.EMobileConfirmationType.FeatureOptOut => _isRussian ? "Изменение функции" : "Feature change",
        _ => _isRussian ? "Действие Steam" : "Steam action"
    };
    public string DetailsActionLabel => IsDetailsVisible
        ? (_isRussian ? "Скрыть" : "Hide")
        : (_isRussian ? "Подробнее" : "Details");
    public string TradeDirectionLabel => _isRussian ? "Отправитель и получатель" : "Sender and receiver";
    public string GivingLabel => _isRussian ? "Вы отдаёте" : "You give";
    public string ReceivingLabel => _isRussian ? "Вы получаете" : "You receive";
    public string NothingLabel => _isRussian ? "Нет предметов" : "No items";
    public ObservableCollection<ConfirmationTradeItemViewModel> GivingItems { get; } = [];
    public ObservableCollection<ConfirmationTradeItemViewModel> ReceivingItems { get; } = [];
    public bool HasGivingItems => GivingItems.Count > 0;
    public bool HasReceivingItems => ReceivingItems.Count > 0;
    public bool HasDetailsError => !string.IsNullOrWhiteSpace(DetailsText);

    [ObservableProperty]
    private IImage? _senderAvatar;

    [ObservableProperty]
    private IImage? _receiverAvatar;

    [ObservableProperty]
    private bool _isDetailsVisible;

    [ObservableProperty]
    private bool _isDetailsBusy;

    [ObservableProperty]
    private string _detailsText = string.Empty;

    public async Task LoadAvatarsAsync()
    {
        Task<IImage?> sender = SteamAvatarLoader.LoadUrlAsync(Confirmation.SenderAvatarUrl);
        Task<IImage?> receiver = SteamAvatarLoader.LoadUrlAsync(Confirmation.ReceiverAvatarUrl);
        await Task.WhenAll(sender, receiver);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SenderAvatar = sender.Result;
            ReceiverAvatar = receiver.Result;
        });
    }

    public void ToggleLoadedDetails() => IsDetailsVisible = !IsDetailsVisible;

    public void SetDetailsLoading(bool loading)
    {
        IsDetailsBusy = loading;
        if (loading) IsDetailsVisible = true;
    }

    public void SetDetails(ConfirmationDetailsResult result)
    {
        IsDetailsBusy = false;
        GivingItems.Clear();
        ReceivingItems.Clear();
        if (result.Success)
        {
            foreach (ConfirmationTradeItem item in result.Items)
            {
                var viewModel = new ConfirmationTradeItemViewModel(item);
                (item.Side == ConfirmationTradeSide.Give ? GivingItems : ReceivingItems).Add(viewModel);
                _ = viewModel.LoadImageAsync();
            }
            DetailsText = string.Empty;
        }
        else
        {
            DetailsText = _isRussian ? "Не удалось загрузить состав обмена." : "Could not load trade contents.";
        }
        OnPropertyChanged(nameof(HasGivingItems));
        OnPropertyChanged(nameof(HasReceivingItems));
        IsDetailsVisible = true;
    }

    public void SetLanguage(bool isRussian)
    {
        _isRussian = isRussian;
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(SenderInitial));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(DetailsActionLabel));
        OnPropertyChanged(nameof(TradeDirectionLabel));
        OnPropertyChanged(nameof(GivingLabel));
        OnPropertyChanged(nameof(ReceivingLabel));
        OnPropertyChanged(nameof(NothingLabel));
    }

    partial void OnSenderAvatarChanged(IImage? value)
    {
        OnPropertyChanged(nameof(HasSenderAvatar));
        OnPropertyChanged(nameof(ShowSenderFallback));
    }

    partial void OnReceiverAvatarChanged(IImage? value)
    {
        OnPropertyChanged(nameof(HasReceiverAvatar));
        OnPropertyChanged(nameof(ShowReceiverFallback));
    }

    partial void OnIsDetailsVisibleChanged(bool value) => OnPropertyChanged(nameof(DetailsActionLabel));

    partial void OnDetailsTextChanged(string value) => OnPropertyChanged(nameof(HasDetailsError));

}

public partial class ConfirmationTradeItemViewModel(ConfirmationTradeItem item) : ViewModelBase
{
    public string Name => item.Name;

    [ObservableProperty]
    private IImage? _image;

    public async Task LoadImageAsync()
    {
        IImage? image = await SteamAvatarLoader.LoadUrlAsync(item.ImageUrl);
        await Dispatcher.UIThread.InvokeAsync(() => Image = image);
    }
}
