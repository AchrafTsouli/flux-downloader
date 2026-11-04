using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace SwiftFetch;
public sealed class TextEntryDialog : Window
{
    public string Value { get; private set; } = "";
    public TextEntryDialog(string title, string label, string initial = "", bool password = false, bool multiline = false)
    {
        Title = title; Width = 620; Height = multiline ? 430 : 220; Background = (Brush)Application.Current.Resources["ThemeBackground1E1E1E"]; Foreground = Brushes.White; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"ThemeBackground1B1B1B"); SetResourceReference(ForegroundProperty,"ThemeForegroundDDDDDD");
        var panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
        var text = new TextBox { Text = initial, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = multiline ? 260 : 32, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var secret = new PasswordBox { Password = initial, Height = 32 }; panel.Children.Add(password ? secret : text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) };
        var ok = new Button { Content = "OK", IsDefault = true }; var cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => { Value = password ? secret.Password : text.Text; DialogResult = true; }; buttons.Children.Add(cancel); buttons.Children.Add(ok); panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => { if (password) secret.Focus(); else { text.Focus(); text.SelectAll(); } };
    }
}
