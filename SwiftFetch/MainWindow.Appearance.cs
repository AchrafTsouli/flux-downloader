using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
namespace SwiftFetch;
public partial class MainWindow
{
    private TrayIcon? tray;
    public void EnableBackgroundTray() { settings.ShowTrayIcon = true; EnableTray(); SaveHistory(); }
    private void Tray_Click(object sender, RoutedEventArgs e)
    {
        settings.ShowTrayIcon = ((MenuItem)sender).IsChecked;
        if (settings.ShowTrayIcon) EnableTray(); else { tray?.Dispose(); tray = null; } SaveHistory();
    }
    private void EnableTray()
    {
        if (closing || canClose) return;
        try { tray ??= new TrayIcon(this, ShowTrayDownloads, rightClick: ShowTrayDownloads); }
        catch (Exception error) { MessageText.Text = "Notification area unavailable: " + error.Message; if (!IsVisible) { Show(); WindowState = WindowState.Normal; } }
    }
    private void FileMenu_Opened(object sender, RoutedEventArgs e)
    {
        var row = DownloadGrid.SelectedItem as DownloadRow;
        foreach (var item in ((MenuItem)sender).Items.OfType<MenuItem>()) item.IsEnabled = (item.Tag as string) switch { "pause" => SelectedRows().Any(r => r.CanPause), "resume" => SelectedRows().Any(r => r.CanResume), "remove" => SelectedRows().Any(r => r.CanRemove), "redownload" => row != null && !row.CanPause, "open" => row?.CanOpen == true, "rename" => row?.CanRename == true, _ => row != null };
    }
    private void DownloadMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender; var rows = SelectedRows(); if (rows.Length == 0) { menu.IsOpen = false; return; }
        foreach (var item in menu.Items.OfType<MenuItem>())
            if (item.Header as string is "Resume" or "Reprendre") item.IsEnabled = rows.Any(r => r.CanResume);
            else if (item.Header as string is "Stop" or "Arrêter") item.IsEnabled = rows.Any(r => r.CanPause);
            else if (item.Header as string is "Delete" or "Retirer") item.IsEnabled = rows.Any(r => r.CanRemove);
            else if (item.Header as string is "Assign to queue…" or "Affecter à une file…") item.IsEnabled = rows.Any(r => r.CanRemove && !scheduler.IsRunning(r.Job));
    }
    private void DownloadSelection_Changed(object sender,SelectionChangedEventArgs e) => UpdateSelectedActions();
    private void UpdateSelectedActions()
    {
        if (DownloadGrid == null || ResumeSelectionButton == null) return;
        StopAllToolButton.IsEnabled = Downloads.Any(r => r.CanPause);
        var rows = SelectedRows(); ResumeSelectionButton.IsEnabled = rows.Any(r => r.CanResume); PauseSelectionButton.IsEnabled = rows.Any(r => r.CanPause); RemoveSelectionButton.IsEnabled = rows.Any(r => r.CanRemove);
    }
    private async Task DisconnectVpnAsync()
    {
        await vpnGate.WaitAsync();
        try { if (scheduler.ActiveCount == 0 && vpnConnected) { await RasDialAsync(true, lifetime.Token); vpnConnected = false; } } catch (Exception error) { MessageText.Text = error.Message; }
        finally { vpnGate.Release(); }
    }
    private void Theme_Click(object sender, RoutedEventArgs e) { settings.DarkMode = ((MenuItem)sender).IsChecked; ApplyTheme(); SaveHistory(); }
    private void Language_Click(object sender, RoutedEventArgs e) { settings.Language = (string)((MenuItem)sender).Tag; ApplyLanguage(); SaveHistory(); }
    private readonly Dictionary<MenuItem,string> originalHeaders = [];
    private readonly Dictionary<string,string> french = new()
    {
        ["_Tasks"]="_Tâches", ["_File"]="_Fichier", ["_Downloads"]="_Téléchargements", ["_View"]="_Affichage", ["_Help"]="_Aide",
        ["Add new download"]="Ajouter un téléchargement", ["Add batch download"]="Ajouter un lot", ["Add batch download from clipboard"]="Ajouter un lot depuis le presse-papiers", ["Run Site Grabber"]="Collecter les liens d’un site", ["Show drop target"]="Afficher la cible de dépôt", ["Export download links…"]="Exporter les liens…", ["Import download links…"]="Importer des liens…", ["Exit"]="Quitter",
        ["Stop download"]="Arrêter le téléchargement", ["Remove"]="Retirer", ["Download now / Resume"]="Télécharger / Reprendre", ["Redownload"]="Télécharger à nouveau", ["Open file"]="Ouvrir le fichier", ["Show in folder"]="Afficher dans le dossier", ["Rename"]="Renommer",
        ["Pause all"]="Tout mettre en pause", ["Stop all"]="Tout arrêter", ["Resume all"]="Tout reprendre", ["Delete all completed"]="Retirer les terminés", ["Find"]="Rechercher", ["Find next"]="Résultat suivant", ["Scheduler"]="Planificateur", ["Start queue…"]="Démarrer une file…", ["Stop queue…"]="Arrêter une file…", ["Speed limiter…"]="Limiteur de vitesse…", ["Options"]="Options", ["Categories panel"]="Catégories", ["Arrange files"]="Trier les fichiers", ["File name"]="Nom du fichier", ["Size"]="Taille", ["Status"]="État", ["Date added"]="Date d’ajout", ["Toolbar"]="Barre d’outils", ["Tray icon"]="Icône de notification", ["Customize URL list…"]="Personnaliser les colonnes…", ["Dark mode"]="Mode sombre", ["Font…"]="Police…", ["Language"]="Langue", ["Download details"]="Détails", ["Help contents"]="Sommaire de l’aide", ["Tutorials"]="Guide de démarrage", ["Scheduler and queues"]="Planificateur et files", ["Grabber help"]="Aide du collecteur", ["Tip of the day"]="Astuce", ["Flux project information"]="Informations sur Flux", ["Support diagnostics"]="Diagnostic", ["Check for updates"]="Vérifier les mises à jour", ["About Flux"]="À propos d’Flux", ["Tell a friend"]="Partager", ["Resume"]="Reprendre", ["Stop"]="Arrêter", ["Download progress dialog"]="Fenêtre de progression", ["Copy download link"]="Copier le lien", ["Assign to queue…"]="Affecter à une file…", ["Login / session"]="Connexion / session", ["Inspect remote ZIP archive"]="Inspecter le ZIP distant", ["Compute SHA-256 Checksum"]="Calculer SHA-256", ["Delete"]="Retirer"
    };
    private void ApplyLanguage()
    {
        void Translate(MenuItem item) { if (!originalHeaders.ContainsKey(item)) originalHeaders[item] = item.Header as string ?? ""; string header = originalHeaders[item]; item.Header = settings.Language == "fr" ? french.GetValueOrDefault(header,header) : header; foreach (var child in item.Items.OfType<MenuItem>()) Translate(child); }
        foreach (var item in MainMenu.Items.OfType<MenuItem>()) Translate(item);
        foreach (var item in DownloadGrid.ContextMenu.Items.OfType<MenuItem>()) Translate(item);
    }
    private void ApplyTheme()
    {
        ApplyCaptionTheme();
        foreach (var key in Application.Current.Resources.Keys.OfType<string>().Where(key => key.StartsWith("Theme")).ToArray())
        {
            string role = key[5..^6]; string hex = key[^6..];
            var original = (Color)ColorConverter.ConvertFromString("#" + hex);
            Application.Current.Resources[key] = new SolidColorBrush(GlacierPalette.Map(original, settings.DarkMode));
        }
        bool dark = settings.DarkMode;
        var resources = Application.Current.Resources;
        Color accent = dark ? GlacierPalette.AccentDark : GlacierPalette.AccentLight, accentAlt = dark ? GlacierPalette.AccentAltDark : GlacierPalette.AccentAltLight;
        Color selection = dark ? GlacierPalette.SelectionDark : GlacierPalette.SelectionLight, selectionText = dark ? GlacierPalette.SelectionTextDark : GlacierPalette.SelectionTextLight;
        resources["Accent"] = new SolidColorBrush(accent);
        resources["AccentAlt"] = new SolidColorBrush(accentAlt);
        resources["AccentGradient"] = new LinearGradientBrush(accent, accentAlt, 0);
        resources["Selection"] = new SolidColorBrush(selection);
        resources["StatusGood"] = new SolidColorBrush(GlacierPalette.Hex(dark ? "#4ADE80" : "#15803D"));
        resources["StatusBad"] = new SolidColorBrush(GlacierPalette.Hex(dark ? "#F87171" : "#B91C1C"));
        resources["StatusWarn"] = new SolidColorBrush(GlacierPalette.Hex(dark ? "#FBBF24" : "#B45309"));
        resources["SelectionText"] = new SolidColorBrush(selectionText);
        resources[SystemColors.HighlightBrushKey] = new SolidColorBrush(selection);
        resources[SystemColors.InactiveSelectionHighlightBrushKey] = new SolidColorBrush(selection);
        resources[SystemColors.HighlightTextBrushKey] = new SolidColorBrush(selectionText);
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = new SolidColorBrush(selectionText);
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root; int count = VisualTreeHelper.GetChildrenCount(root); for (int i = 0; i < count; i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}
internal sealed class TrayIcon : IDisposable
{
    private const int Callback = 0x8001;
    private readonly HwndSource source;
    private readonly Action show;
    private readonly Action? rightClick;
    private NotifyData data;
    private bool ownsIcon;
    private bool disposed;
    internal static int ActiveIconCount { get; private set; }
    internal bool UsesApplicationIcon => ownsIcon;
    private readonly int taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    [DllImport("user32.dll",CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id; public uint Flags; public uint Message; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ExtractIcon(IntPtr instance, string path, uint index);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    public TrayIcon(Window window, Action show, string? tip = null, Action? rightClick = null)
    {
        this.show = show; this.rightClick = rightClick; var handle = new WindowInteropHelper(window).EnsureHandle(); source = HwndSource.FromHwnd(handle)!; source.AddHook(Hook);
        string iconFile = System.IO.Path.Combine(AppContext.BaseDirectory,"app.ico");
        string iconPath = System.IO.File.Exists(iconFile) ? iconFile : Environment.ProcessPath ?? iconFile;
        IntPtr icon = ExtractIcon(IntPtr.Zero,iconPath,0); ownsIcon = icon.ToInt64() > 1;
        if (!ownsIcon) icon = LoadIcon(IntPtr.Zero,new IntPtr(32512));
        string tooltip = tip ?? "Flux — click to open";
        data = new() { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = handle, Id = 1, Flags = 1|2|4, Message = Callback, Icon = icon, Tip = tooltip[..Math.Min(tooltip.Length,127)], Info = "", Title = "" };
        if (!Shell_NotifyIcon(0, ref data)) { source.RemoveHook(Hook); if (ownsIcon) DestroyIcon(data.Icon); throw new InvalidOperationException("Could not add the tray icon."); }
        ActiveIconCount++;
    }
    public void UpdateTip(string value) { string tip = value[..Math.Min(value.Length,127)]; if (data.Tip == tip) return; data.Tip = tip; Shell_NotifyIcon(1,ref data); }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
    { if (message == taskbarCreated && !disposed) Shell_NotifyIcon(0,ref data); if (message == Callback && (l.ToInt64() == 0x202 || l.ToInt64() == 0x203)) { show(); handled = true; } else if (message == Callback && l.ToInt64() == 0x205) { rightClick?.Invoke(); handled = true; } return IntPtr.Zero; }
    public void Dispose() { if (disposed) return; disposed = true; ActiveIconCount--; Shell_NotifyIcon(2,ref data); source.RemoveHook(Hook); if (ownsIcon) { DestroyIcon(data.Icon); ownsIcon = false; } }
}

