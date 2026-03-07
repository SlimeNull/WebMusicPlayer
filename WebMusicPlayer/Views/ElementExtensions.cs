namespace WebMusicPlayer.Views;

internal static class ElementExtensions
{
    public static Page? GetParentPage(this Element element)
    {
        Element? current = element;
        while (current is not null)
        {
            if (current is Page page)
            {
                return page;
            }

            current = current.Parent;
        }

        return null;
    }
}
