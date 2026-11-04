using System.Windows;
using System.Windows.Controls;
namespace SwiftFetch;

public sealed class DeleteDownloadsDialog : Window
{
    public CheckBox DeleteFilesBox { get; } = new() { Content = "Also delete completed files from disk (partial downloads are kept)" };
    public CheckBox DontAskBox { get; } = new() { Content = "Don't show this dialog again" };
    public bool DeleteCompletedFiles => DeleteFilesBox.IsChecked == true;
    public bool DontAskAgain => DontAskBox.IsChecked == true;
    public DeleteDownloadsDialog(int count)
    {
        Title = "Confirm deletion of downloads"; Width = 570; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"ThemeBackground1B1B1B"); SetResourceReference(ForegroundProperty,"ThemeForegroundDDDDDD");
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = $"Remove {count} selected download(s) from the download list?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
        panel.Children.Add(DeleteFilesBox); panel.Children.Add(DontAskBox);
        panel.Children.Add(new TextBlock { Text = "Without confirmation, only list entries are removed. You can enable confirmation again in Options → Downloads.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,14) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        var yes = new Button { Content = "Yes", MinWidth = 95 }; yes.Click += (_,_) => DialogResult = true;
        var no = new Button { Content = "No", MinWidth = 95, IsCancel = true, IsDefault = true }; buttons.Children.Add(yes); buttons.Children.Add(no);
    }
}
