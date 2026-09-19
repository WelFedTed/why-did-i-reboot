# Renders the app icon (a circular "restart" arrow with a question mark) and packs it into a multi-size .ico.
# Usage: pwsh tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\WhyDidIReboot\Assets\app.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, $size * 0.03)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 37, 99, 235))
    $g.FillEllipse($bg, $rect)

    # Circular arrow: an arc with a gap at the top-right and an arrowhead at its end.
    $stroke = [Math]::Max(1.5, $size * 0.085)
    $inset = $size * 0.21
    $arcRect = New-Object System.Drawing.RectangleF $inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, $arcRect, -50, 290)

    # Arrowhead at angle -50 degrees (end of arc), pointing clockwise.
    $cx = $size / 2; $cy = $size / 2; $r = ($size - 2 * $inset) / 2
    $a = -50 * [Math]::PI / 180
    $ex = $cx + $r * [Math]::Cos($a); $ey = $cy + $r * [Math]::Sin($a)
    $tangent = $a + [Math]::PI / 2     # direction of travel (counter to arc sweep)
    $len = $size * 0.17
    $tipX = $ex + $len * [Math]::Cos($tangent) * -1; $tipY = $ey + $len * [Math]::Sin($tangent) * -1
    $side = $size * 0.11
    $p1 = New-Object System.Drawing.PointF ($ex + $side * [Math]::Cos($a)), ($ey + $side * [Math]::Sin($a))
    $p2 = New-Object System.Drawing.PointF ($ex - $side * [Math]::Cos($a)), ($ey - $side * [Math]::Sin($a))
    $tip = New-Object System.Drawing.PointF $tipX, $tipY
    $white = [System.Drawing.Brushes]::White
    $g.FillPolygon($white, [System.Drawing.PointF[]]@($p1, $tip, $p2))

    # Question mark in the middle.
    if ($size -ge 24) {
        $font = New-Object System.Drawing.Font 'Segoe UI', ($size * 0.42), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
        $g.DrawString('?', $font, $white, (New-Object System.Drawing.RectangleF 0, ($size * 0.02), $size, $size), $fmt)
        $font.Dispose()
    }

    $g.Dispose(); $pen.Dispose(); $bg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    # The leading comma stops PowerShell unrolling the byte[] into an object[].
    return ,[byte[]]$ms.ToArray()
}

$images = @()
foreach ($s in $sizes) { $images += ,@{ Size = $s; Bytes = (Render $s) } }

$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([Byte]$dim); $w.Write([Byte]$dim); $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$img.Bytes.Length); $w.Write([UInt32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img.Bytes) }
$w.Flush()
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $out)).Path + '\app.ico', $ico.ToArray())
Write-Host "Wrote $out ($($ico.Length) bytes, $($images.Count) sizes)"
