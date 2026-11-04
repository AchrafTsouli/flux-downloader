using System.Text;
using System.Windows;
using SwiftFetch.Core;
namespace SwiftFetch;
public partial class SessionWindow : Window
{
    private readonly DownloadJob job;
    public DownloadSession? Session { get; private set; }
    public SessionWindow(DownloadJob job)
    {
        this.job = job;
        InitializeComponent();
        OriginText.Text = new Uri(job.Url).GetLeftPart(UriPartial.Authority);
        CookieText.Text = "Browser session: " + (job.Session?.Cookies.Count ?? 0) + " cookies";
        Closed += (_, _) => SecretBox.Clear();
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ModeBox.SelectedIndex == 3) Session = null;
        else
        {
            if (ModeBox.SelectedIndex is 1 or 2 && SecretBox.Password.Length == 0) { ErrorText.Text = "Enter a password or token."; return; }
            if (ModeBox.SelectedIndex == 1 && UsernameBox.Text.Contains(':')) { ErrorText.Text = "Basic usernames cannot contain a colon."; return; }
            Session = new() { Origin = new Uri(job.Url).GetLeftPart(UriPartial.Authority), Referer = job.Session?.Referer, UserAgent = job.Session?.UserAgent, Cookies = job.Session?.Cookies.ToList() ?? [] };
            Session.Authorization = ModeBox.SelectedIndex switch
            {
                1 => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(UsernameBox.Text + ":" + SecretBox.Password)),
                2 => "Bearer " + SecretBox.Password,
                _ => job.Session?.Authorization
            };
            try { Session.Validate(job.Url); } catch (ArgumentException) { ErrorText.Text = "Invalid login data."; return; }
        }
        SecretBox.Clear();
        DialogResult = true;
    }
}
