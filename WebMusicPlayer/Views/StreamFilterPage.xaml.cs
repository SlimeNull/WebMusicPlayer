using WebMusicPlayer.Models;
using Microsoft.Maui.Controls.Shapes;

namespace WebMusicPlayer.Views;

public partial class StreamFilterPage : ContentPage
{
    private readonly TaskCompletionSource<StreamFilterResult?> _resultSource = new();
    private readonly List<(FilterOption Option, Border Border, RadioButton Radio)> _items = [];
    private bool _isClosing;
    private string _selectedSourceKey;

    public StreamFilterPage(IEnumerable<FilterOption> filters, string selectedSourceKey, string? keyword)
    {
        InitializeComponent();
        _selectedSourceKey = string.IsNullOrWhiteSpace(selectedSourceKey) ? FilterOption.AllKey : selectedSourceKey;
        KeywordEntry.Text = keyword ?? string.Empty;
        BuildFilterOptions(filters);
    }

    public static async Task<StreamFilterResult?> ShowAsync(Page page, IEnumerable<FilterOption> filters, string selectedSourceKey, string? keyword)
    {
        var filterPage = new StreamFilterPage(filters, selectedSourceKey, keyword);
        await page.Navigation.PushModalAsync(filterPage, false);
        return await filterPage._resultSource.Task;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(60);
            KeywordEntry.Focus();
        });
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private void BuildFilterOptions(IEnumerable<FilterOption> filters)
    {
        SourcesContainer.Children.Clear();
        _items.Clear();

        foreach (var filter in filters)
        {
            var radio = new RadioButton
            {
                GroupName = "StreamSources",
                Content = filter.Label,
                IsChecked = string.Equals(filter.Key, _selectedSourceKey, StringComparison.OrdinalIgnoreCase),
                Value = filter.Key,
                TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Color.FromArgb("#242424")
            };

            var border = new Border
            {
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#DDD7FA"),
                BackgroundColor = Color.FromArgb("#FCFBFF"),
                Padding = new Thickness(8, 6),
                Content = radio,
                StrokeShape = new RoundRectangle { CornerRadius = 12 }
            };

            radio.CheckedChanged += (_, args) =>
            {
                if (!args.Value)
                {
                    return;
                }

                _selectedSourceKey = filter.Key;
                UpdateSelectionStyles();
            };

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (_, _) => radio.IsChecked = true;
            border.GestureRecognizers.Add(tapGesture);

            SourcesContainer.Children.Add(border);
            _items.Add((filter, border, radio));
        }

        UpdateSelectionStyles();
    }

    private void UpdateSelectionStyles()
    {
        foreach (var item in _items)
        {
            var isSelected = string.Equals(item.Option.Key, _selectedSourceKey, StringComparison.OrdinalIgnoreCase);
            item.Border.BackgroundColor = isSelected
                ? Color.FromArgb("#EEE7FF")
                : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#23232D") : Color.FromArgb("#FCFBFF"));
            item.Border.Stroke = isSelected
                ? Color.FromArgb("#512BD4")
                : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#3B3453") : Color.FromArgb("#DDD7FA"));
        }
    }

    private async void OnResetClicked(object? sender, EventArgs e)
    {
        _selectedSourceKey = FilterOption.AllKey;
        KeywordEntry.Text = string.Empty;

        foreach (var item in _items)
        {
            item.Radio.IsChecked = string.Equals(item.Option.Key, FilterOption.AllKey, StringComparison.OrdinalIgnoreCase);
        }

        UpdateSelectionStyles();
        await Task.CompletedTask;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await CloseAsync(null);
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        await CloseAsync(new StreamFilterResult(_selectedSourceKey, KeywordEntry.Text?.Trim() ?? string.Empty));
    }

    private async Task CloseAsync(StreamFilterResult? result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _resultSource.TrySetResult(result);

        if (Navigation.ModalStack.LastOrDefault() == this)
        {
            await Navigation.PopModalAsync(false);
        }
    }
}
