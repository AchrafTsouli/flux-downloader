using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace SwiftFetch;

/// <summary>Mouse marquee selection over full rows, including virtualized rows reached by scrolling.</summary>
internal sealed class DownloadMarqueeSelection
{
    private readonly DataGrid grid;
    private readonly DispatcherTimer scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private Point origin,lastPoint;
    private int anchor;
    private double initialOffset;
    private bool pending,dragging,capturePointer = true;
    internal bool IsDragging => dragging;
    internal bool IsSelecting => pending;
    private SelectionAdorner? adorner;
    private AdornerLayer? layer;
    private ScrollViewer? scroll;
    public DownloadMarqueeSelection(DataGrid grid)
    {
        this.grid = grid;
        grid.PreviewMouseLeftButtonDown += MouseDown;
        grid.PreviewMouseMove += MouseMove;
        grid.PreviewMouseLeftButtonUp += MouseUp;
        grid.LostMouseCapture += (_,_) => { if (pending && !grid.IsMouseCaptured) Finish(); };
        grid.Unloaded += (_,_) => Finish();
        ((System.Collections.Specialized.INotifyCollectionChanged)grid.Items).CollectionChanged += (_,_) => Finish();
        scrollTimer.Tick += (_,_) => AutoScroll();
    }
    private static T? Ancestor<T>(DependencyObject? value) where T : DependencyObject
    {
        while (value != null) { if (value is T result) return result; value = value is Visual ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value); }
        return null;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject value) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++)
        {
            var child = VisualTreeHelper.GetChild(value,i); if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private void MouseDown(object sender,MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || Keyboard.Modifiers != ModifierKeys.None) { Finish(); return; }
        if (e.OriginalSource is not DependencyObject source || Ancestor<DataGridColumnHeader>(source) != null || Ancestor<ScrollBar>(source) != null || Ancestor<ButtonBase>(source) != null) return;
        var point = e.GetPosition(grid); if (!Body().Contains(point) || grid.Items.Count == 0) return;
        Begin(point); e.Handled = true;
    }
    internal void Begin(Point point,bool capturePointer = true)
    {
        Finish(); grid.Focus(); grid.UpdateLayout();
        var viewer = Descendants<ScrollViewer>(grid).FirstOrDefault();
        if (scroll != viewer) { if (scroll != null) scroll.ScrollChanged -= ScrollChanged; scroll = viewer; if (scroll != null) scroll.ScrollChanged += ScrollChanged; }
        this.capturePointer = capturePointer;
        origin = lastPoint = point; anchor = IndexAt(point); initialOffset = scroll?.VerticalOffset ?? 0; pending = true;
        if (capturePointer) grid.CaptureMouse();
        grid.SelectedItems.Clear();
        if (anchor >= 0 && anchor < grid.Items.Count)
        {
            var item = grid.Items[anchor]; grid.SelectedItems.Add(item);
            var column = grid.CurrentColumn ?? grid.Columns.FirstOrDefault(c => c.Visibility == Visibility.Visible);
            if (column != null) grid.CurrentCell = new DataGridCellInfo(item,column);
        }
    }
    private void MouseMove(object sender,MouseEventArgs e)
    {
        if (!pending) return;
        if (e.LeftButton != MouseButtonState.Pressed) { Finish(); return; }
        Update(e.GetPosition(grid)); if (dragging) e.Handled = true;
    }
    internal void Update(Point point)
    {
        if (!pending) return; lastPoint = point;
        if (!dragging)
        {
            if (Math.Abs(point.X-origin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragging = true; if (capturePointer) grid.CaptureMouse();
            layer = AdornerLayer.GetAdornerLayer(grid); if (layer != null) { adorner = new(grid) { IsHitTestVisible = false }; layer.Add(adorner); }
            scrollTimer.Start();
        }
        SelectRange();
    }
    private Rect Body()
    {
        var header = Descendants<DataGridColumnHeadersPresenter>(grid).FirstOrDefault();
        double top = header == null ? 0 : header.TranslatePoint(new Point(0,header.ActualHeight),grid).Y;
        double bottom = grid.ActualHeight;
        var horizontal = Descendants<ScrollBar>(grid).FirstOrDefault(bar => bar.Orientation == Orientation.Horizontal && bar.IsVisible);
        if (horizontal != null) bottom = Math.Min(bottom,horizontal.TranslatePoint(new Point(),grid).Y);
        return new Rect(0,Math.Max(0,top),Math.Max(0,grid.ActualWidth),Math.Max(0,bottom-top));
    }
    private int IndexAt(Point point)
    {
        var row = Descendants<DataGridRow>(grid).FirstOrDefault(value => value.IsVisible && value.ActualHeight > 0 && value.GetIndex() >= 0);
        if (row == null) return -1;
        double height = row.ActualHeight, top = row.TranslatePoint(new Point(),grid).Y;
        return row.GetIndex() + (int)Math.Floor((point.Y-top)/height);
    }
    private void SelectRange()
    {
        int end = IndexAt(lastPoint), first = Math.Max(0,Math.Min(anchor,end)), last = Math.Min(grid.Items.Count-1,Math.Max(anchor,end));
        var wanted = first <= last ? Enumerable.Range(first,last-first+1).Select(i => grid.Items[i]).ToHashSet() : [];
        foreach (var item in grid.SelectedItems.Cast<object>().Where(item => !wanted.Contains(item)).ToArray()) grid.SelectedItems.Remove(item);
        foreach (var item in wanted.Where(item => !grid.SelectedItems.Contains(item))) grid.SelectedItems.Add(item);
        if (adorner != null)
        {
            double height = grid.RowHeight is > 0 && double.IsFinite(grid.RowHeight) ? grid.RowHeight : 34;
            double offset = ((scroll?.VerticalOffset ?? 0)-initialOffset) * (scroll?.CanContentScroll == true ? height : 1);
            var area = new Rect(new Point(origin.X,origin.Y-offset),lastPoint); area.Intersect(Body()); adorner.Area = area; adorner.InvalidateVisual();
        }
    }
    private void ScrollChanged(object sender,ScrollChangedEventArgs e)
    {
        if (dragging && e.VerticalChange != 0) grid.Dispatcher.BeginInvoke(() => { if (dragging) SelectRange(); },DispatcherPriority.Loaded);
    }
    private void AutoScroll()
    {
        if (!dragging || scroll == null) return;
        var body = Body(); double before = scroll.VerticalOffset;
        if (lastPoint.Y < body.Top+18) scroll.LineUp(); else if (lastPoint.Y > body.Bottom-18) scroll.LineDown();
        if (scroll.VerticalOffset != before) { grid.UpdateLayout(); SelectRange(); }
    }
    private void MouseUp(object sender,MouseButtonEventArgs e) { if (!pending) return; e.Handled = true; Finish(); }
    internal void Finish()
    {
        pending = dragging = false; scrollTimer.Stop();
        if (adorner != null) { layer?.Remove(adorner); adorner = null; }
        if (grid.IsMouseCaptured) grid.ReleaseMouseCapture();
    }
    private sealed class SelectionAdorner(UIElement element) : Adorner(element)
    {
        public Rect Area { get; set; }
        protected override void OnRender(DrawingContext drawingContext)
        {
            IsHitTestVisible = false; if (Area.IsEmpty) return;
            var accent = (Application.Current?.TryFindResource("Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(56,189,248);
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(35,accent.R,accent.G,accent.B)),new Pen(new SolidColorBrush(accent),1),Area);
        }
    }
}

