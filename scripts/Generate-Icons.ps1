# Renders the Flux icon (progress ring around a download arrow, Glacier gradient) at every size the
# app, installer, tray and browser extension use, and writes a multi-size app.ico.
# Artwork is defined on a 64-unit grid, matching the approved design.
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

function New-Color([string]$hex, [byte]$alpha = 255) {
    $c = [System.Windows.Media.ColorConverter]::ConvertFromString($hex)
    return [System.Windows.Media.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}

function Render-FluxIcon([int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $ctx = $visual.RenderOpen()
    $k = $size / 64.0
    # Small sizes get slightly heavier strokes so the ring and arrow stay legible in the tray.
    $stroke = 5.0 * $k * $(if ($size -le 16) { 1.3 } elseif ($size -le 24) { 1.15 } else { 1.0 })

    $gradient = [System.Windows.Media.LinearGradientBrush]::new((New-Color '#38BDF8'), (New-Color '#6366F1'), ([System.Windows.Point]::new(0,0)), ([System.Windows.Point]::new(1,1)))
    $radius = 15 * $k
    $ctx.DrawRoundedRectangle($gradient, $null, ([System.Windows.Rect]::new(0, 0, $size, $size)), $radius, $radius)

    $white = [System.Windows.Media.SolidColorBrush]::new((New-Color '#FFFFFF'))
    $faint = [System.Windows.Media.SolidColorBrush]::new((New-Color '#FFFFFF' 72))
    $pen = [System.Windows.Media.Pen]::new($white, $stroke)
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
    $track = [System.Windows.Media.Pen]::new($faint, $stroke)

    # Full faint track, then the three-quarter progress arc from 12 o'clock clockwise to 9 o'clock.
    $center = [System.Windows.Point]::new(32 * $k, 32 * $k)
    $ctx.DrawEllipse($null, $track, $center, 19 * $k, 19 * $k)
    $arc = New-Object System.Windows.Media.PathFigure
    $arc.StartPoint = [System.Windows.Point]::new(32 * $k, 13 * $k)
    $arc.Segments.Add(([System.Windows.Media.ArcSegment]::new(([System.Windows.Point]::new(13 * $k, 32 * $k)), ([System.Windows.Size]::new(19 * $k, 19 * $k)), 0, $true, 'Clockwise', $true)))
    $arcGeometry = New-Object System.Windows.Media.PathGeometry
    $arcGeometry.Figures.Add($arc)
    $ctx.DrawGeometry($null, $pen, $arcGeometry)

    # Download arrow: shaft and head.
    $ctx.DrawLine($pen, ([System.Windows.Point]::new(32 * $k, 23 * $k)), ([System.Windows.Point]::new(32 * $k, 40 * $k)))
    $head = New-Object System.Windows.Media.PathFigure
    $head.StartPoint = [System.Windows.Point]::new(25 * $k, 34 * $k)
    $head.Segments.Add(([System.Windows.Media.LineSegment]::new(([System.Windows.Point]::new(32 * $k, 41 * $k)), $true)))
    $head.Segments.Add(([System.Windows.Media.LineSegment]::new(([System.Windows.Point]::new(39 * $k, 34 * $k)), $true)))
    $headGeometry = New-Object System.Windows.Media.PathGeometry
    $headGeometry.Figures.Add($head)
    $ctx.DrawGeometry($null, $pen, $headGeometry)
    $ctx.Close()

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = New-Object System.IO.MemoryStream
    $encoder.Save($memory)
    return ,$memory.ToArray()
}

function Write-Ico([string]$path, [int[]]$sizes) {
    $images = @(foreach ($s in $sizes) { ,(Render-FluxIcon $s) })
    $stream = [System.IO.File]::Create($path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
        $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([UInt16]1); $writer.Write([UInt16]32)
        $writer.Write([UInt32]$images[$i].Length); $writer.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
    $writer.Dispose()
}

Write-Ico (Join-Path $Root 'SwiftFetch/app.ico') @(16, 20, 24, 32, 40, 48, 64, 128, 256)
[System.IO.File]::WriteAllBytes((Join-Path $Root 'SwiftFetch/app-icon.png'), (Render-FluxIcon 256))
foreach ($s in 16, 32, 48, 128, 256) { [System.IO.File]::WriteAllBytes((Join-Path $Root "BrowserExtension/icon-$s.png"), (Render-FluxIcon $s)) }
Write-Output 'Flux icons written: SwiftFetch/app.ico, SwiftFetch/app-icon.png, BrowserExtension/icon-*.png'
