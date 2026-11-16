# Generates setup\prism.ico (PNG-compressed entries 16..256) for PrismDock.
param([string]$Out = (Join-Path $PSScriptRoot 'prism.ico'))
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $m = [Math]::Max(1.0, $s * 0.04)
    $rect = New-Object System.Drawing.RectangleF $m, $m, ($s - 2 * $m), ($s - 2 * $m)
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(94, 242, 208)), ([System.Drawing.Color]::FromArgb(140, 90, 255)), 45.0
    $g.FillEllipse($br, $rect)
    # soft violet depth at the bottom
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($rect)
    $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $pg.CenterPoint = New-Object System.Drawing.PointF ($s * 0.5), ($s * 0.35)
    $pg.CenterColor = [System.Drawing.Color]::FromArgb(0, 255, 255, 255)
    $pg.SurroundColors = @([System.Drawing.Color]::FromArgb(70, 40, 10, 90))
    $g.FillEllipse($pg, $rect)
    # white sheen
    $sh = New-Object System.Drawing.RectangleF ($s * 0.22), ($s * 0.12), ($s * 0.56), ($s * 0.32)
    $sb = New-Object System.Drawing.Drawing2D.LinearGradientBrush $sh, ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(20, 255, 255, 255)), 90.0
    $g.FillEllipse($sb, $sh)
    # rim
    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 255, 255, 255)), ([float]([Math]::Max(1.0, $s / 64.0)))
        $g.DrawEllipse($pen, $rect)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @(); foreach ($s in $sizes) { $frames += , (New-Frame $s) }
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $frames[$i]
    $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$d.Length); $w.Write([UInt32]$offset)
    $offset += $d.Length
}
foreach ($d in $frames) { $w.Write($d) }
$w.Close()
Write-Host "Icon: $Out"
