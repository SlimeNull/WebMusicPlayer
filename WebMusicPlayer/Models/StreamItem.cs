using CommunityToolkit.Mvvm.ComponentModel;

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
    private bool isFavourite;

    [ObservableProperty]
    private StreamOriginKind originKind;

    [ObservableProperty]
    private Guid? subscriptionId;

    [ObservableProperty]
    private string? subscriptionName;

    public string OriginLabel => OriginKind == StreamOriginKind.Manual
        ? "手动添加"
        : string.IsNullOrWhiteSpace(SubscriptionName) ? "订阅" : SubscriptionName;

    public string FavouriteGlyph => IsFavourite ? "★" : "☆";

    public string FavouriteActionText => IsFavourite ? "取消喜欢" : "标记喜欢";

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
