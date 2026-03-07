using WebMusicPlayer.Views;

namespace WebMusicPlayer
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage)
        {
            InitializeComponent();

            Items.Add(new ShellContent
            {
                Route = "Main",
                Content = mainPage
            });
        }
    }
}
