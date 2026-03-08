using CommunityToolkit.Mvvm.ComponentModel;
using WebMusicPlayer.Localization;

namespace WebMusicPlayer.Models;

public partial class StreamItem : ObservableObject
{
    [ObservableProperty]
    private Guid id = Guid.NewGuid();

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string url = string.Empty;

    [ObservableProperty]
    private string? artworkUrl;

    [ObservableProperty]
    private bool isFavourite;

    [ObservableProperty]
    private StreamOriginKind originKind;

    [ObservableProperty]
    private Guid? subscriptionId;

    [ObservableProperty]
    private string? subscriptionName;

    public string OriginLabel => OriginKind == StreamOriginKind.Manual
        ? TranslateExtension.Get("OriginManualAdded")
        : string.IsNullOrWhiteSpace(SubscriptionName) ? TranslateExtension.Get("OriginSubscription") : SubscriptionName;

    public string FavouriteGlyph => IsFavourite ? AppIcons.FavouriteFilled : AppIcons.Favourite;

    public string FavouriteActionText => IsFavourite ? TranslateExtension.Get("FavouriteActionRemove") : TranslateExtension.Get("FavouriteActionAdd");

    partial void OnOriginKindChanged(StreamOriginKind value) => NotifyComputedProperties();

    partial void OnSubscriptionNameChanged(string? value) => NotifyComputedProperties();

    partial void OnIsFavouriteChanged(bool value) => NotifyComputedProperties();

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(OriginLabel));
        OnPropertyChanged(nameof(FavouriteGlyph));
        OnPropertyChanged(nameof(FavouriteActionText));
    }
}
