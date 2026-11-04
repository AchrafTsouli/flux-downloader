using System.Windows;
namespace SwiftFetch;
public partial class RenameWindow : Window
{
    public string FileName => NameBox.Text;
    public RenameWindow(string name) { InitializeComponent(); NameBox.Text = name; Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); }; }
    private void Save_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(NameBox.Text)) IndependentDialogs.Finish(this,true); }
}
