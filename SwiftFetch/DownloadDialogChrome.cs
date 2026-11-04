using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
namespace SwiftFetch;
internal static class DownloadDialogChrome
{
    public static void Install(Window window)
    {
        var content = (UIElement)window.Content; window.Content = null;
        window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.CanMinimize;
        WindowChrome.SetWindowChrome(window,new WindowChrome { CaptionHeight = 32,ResizeBorderThickness = new Thickness(0),GlassFrameThickness = new Thickness(0),UseAeroCaptionButtons = false });
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new GridLength(32) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = new DockPanel { LastChildFill = true }; var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions,Dock.Right); title.Children.Add(actions);
        Button Action(string label,string tip,Action action)
        {
            var button = new Button { Content = label,ToolTip = tip,Width = 38,Height = 32,MinHeight = 0,Margin = new Thickness(0),Padding = new Thickness(0),BorderThickness = new Thickness(0),Background = Brushes.Transparent };
            WindowChrome.SetIsHitTestVisibleInChrome(button,true); button.Click += (_,_) => action(); actions.Children.Add(button); return button;
        }
        Action("—","Minimize",() => window.WindowState = WindowState.Minimized); Action("×","Close",window.Close);
        var label = new DockPanel { Margin = new Thickness(10,0,6,0),VerticalAlignment = VerticalAlignment.Center };
        var icon = new BitmapImage(new Uri("pack://application:,,,/SwiftFetch;component/app-icon.png")); window.Icon = icon;
        label.Children.Add(new Image { Source = icon,Width = 16,Height = 16,Margin = new Thickness(0,0,7,0) });
        var text = new TextBlock { FontSize = 11.5,TextTrimming = TextTrimming.CharacterEllipsis,VerticalAlignment = VerticalAlignment.Center }; text.SetBinding(TextBlock.TextProperty,new Binding(nameof(Window.Title)) { Source = window }); label.Children.Add(text); title.Children.Add(label);
        var border = new Border { Child = title,BorderThickness = new Thickness(0,0,0,1) }; border.SetResourceReference(Border.BackgroundProperty,"ThemeBackground262626"); border.SetResourceReference(Border.BorderBrushProperty,"ThemeBorderBrush383838"); root.Children.Add(border);
        Grid.SetRow(content,1); root.Children.Add(content); window.Content = root;
    }
}
