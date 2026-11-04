using System.Windows;

namespace SwiftFetch;

public partial class AddUrlDialog : Window
{
    public string Url { get; private set; } = "";
    public string? Username { get; private set; }
    public string? Password { get; private set; }
    public bool TreatAsVideo { get; private set; }

    public AddUrlDialog(string? initialUrl = null)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            string candidate = initialUrl?.Trim() ?? "";
            if (string.IsNullOrEmpty(candidate))
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string clip = Clipboard.GetText().Trim();
                        if (clip.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            clip.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            candidate = clip;
                        }
                    }
                }
                catch { }
            }

            UrlBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new System.Windows.Controls.TextChangedEventHandler((_, _) =>
                VideoCheck.IsChecked = SwiftFetch.Core.VideoPages.IsKnownVideoPage(UrlBox.Text.Trim())));
            if (!string.IsNullOrEmpty(candidate))
            {
                UrlBox.Text = candidate;
                UrlBox.Focus();
            }
            else
            {
                UrlBox.Focus();
            }
        };
    }

    private void UseAuthCheck_Changed(object sender, RoutedEventArgs e)
    {
        bool isAuth = UseAuthCheck.IsChecked == true;
        LoginBox.IsEnabled = isAuth;
        PasswordBox.IsEnabled = isAuth;
        if (isAuth) LoginBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string raw = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(raw) ||
            (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
             !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
             SwiftFetch.Core.Torrents.MagnetHash(raw) == null))
        {
            MessageBox.Show(this, "Enter an HTTP or HTTPS address, or a magnet link, to download.", "Flux", MessageBoxButton.OK, MessageBoxImage.Warning);
            UrlBox.Focus();
            return;
        }

        Url = raw;
        TreatAsVideo = VideoCheck.IsChecked == true;
        if (UseAuthCheck.IsChecked == true)
        {
            Username = string.IsNullOrWhiteSpace(LoginBox.Text) ? null : LoginBox.Text.Trim();
            Password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password;
        }

        IndependentDialogs.Finish(this,true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IndependentDialogs.Finish(this,false);
    }
}
