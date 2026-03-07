using System.Globalization;
using System.Resources;
using Microsoft.Maui.Controls.Xaml;

namespace WebMusicPlayer.Localization;

[ContentProperty(nameof(Key))]
public sealed class TranslateExtension : IMarkupExtension<string>
{
    private static readonly ResourceManager ResourceManager = new("WebMusicPlayer.Resources.Localization.AppResources", typeof(TranslateExtension).Assembly);

    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return string.Empty;
        }

        return ResourceManager.GetString(Key, CultureInfo.CurrentUICulture) ?? Key;
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);

    public static string Get(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    public static string Format(string key, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
    }
}
