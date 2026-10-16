# Generates assets\GraphEditor.ico (multi-size, classic 32-bit DIB frames).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
#
# DIB frames (instead of PNG-compressed ones) are used so that GDI+, Windows
# Explorer and the taskbar all decode the icon correctly. The result is
# committed to the repository; re-run only when the artwork changes.
#
# NOTE: this file must stay ASCII-only, because Windows PowerShell 5.1 reads
# BOM-less script files using the ANSI code page.

[CmdletBinding()]
param(
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutFile) { $OutFile = Join-Path $root 'assets\GraphEditor.ico' }
$OutFile = [System.IO.Path]::GetFullPath($OutFile)

Add-Type -AssemblyName System.Drawing

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$Size
    $inset = [double]($s * 0.045)
    $rect = New-Object System.Drawing.RectangleF($inset, $inset, ($s - 2 * $inset), ($s - 2 * $inset))
    $radius = [float]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float]($radius * 2)
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 0x45, 0x9B, 0xEA),
        [System.Drawing.Color]::FromArgb(255, 0x18, 0x49, 0x82),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # Three vertices connected by three edges (a small graph motif).
    $penWidth = [float]([Math]::Max(1.0, $s * 0.075))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $p1 = New-Object System.Drawing.PointF(([float]($s * 0.29)), ([float]($s * 0.72)))
    $p2 = New-Object System.Drawing.PointF(([float]($s * 0.50)), ([float]($s * 0.25)))
    $p3 = New-Object System.Drawing.PointF(([float]($s * 0.77)), ([float]($s * 0.67)))
    $g.DrawLine($pen, $p1, $p2)
    $g.DrawLine($pen, $p2, $p3)
    $g.DrawLine($pen, $p1, $p3)
    $pen.Dispose()

    $dot = [float]($s * 0.12)
    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $dotPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0x0F, 0x33, 0x5C), [float]([Math]::Max(0.8, $s * 0.04)))
    foreach ($p in @($p1, $p2, $p3)) {
        $g.FillEllipse($dotBrush, ($p.X - $dot), ($p.Y - $dot), ($dot * 2), ($dot * 2))
        $g.DrawEllipse($dotPen, ($p.X - $dot), ($p.Y - $dot), ($dot * 2), ($dot * 2))
    }
    $dotBrush.Dispose()
    $dotPen.Dispose()

    $g.Dispose()
    return $bmp
}

# Convert a Bitmap into the 32-bit DIB used inside an ICO
# (BITMAPINFOHEADER + bottom-up BGRA XOR bitmap + AND mask).
function ConvertTo-IconDib {
    param([System.Drawing.Bitmap]$Bitmap)

    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $stride = $width * 4

    $data = $Bitmap.LockBits(
        (New-Object System.Drawing.Rectangle(0, 0, $width, $height)),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $buffer = New-Object byte[] ($stride * $height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buffer, 0, $buffer.Length)
    $Bitmap.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER
    $w.Write([uint32]40)
    $w.Write([int32]$width)
    $w.Write([int32]($height * 2))     # XOR bitmap + AND mask
    $w.Write([uint16]1)
    $w.Write([uint16]32)
    $w.Write([uint32]0)                # BI_RGB
    $w.Write([uint32]($stride * $height))
    $w.Write([int32]0)
    $w.Write([int32]0)
    $w.Write([uint32]0)
    $w.Write([uint32]0)

    # XOR bitmap: bottom-up BGRA (GDI+ stores Format32bppArgb as BGRA in memory)
    for ($y = $height - 1; $y -ge 0; $y--) {
        $rowStart = $y * $stride
        for ($x = 0; $x -lt $width; $x++) {
            $i = $rowStart + $x * 4
            $w.Write([byte]$buffer[$i])
            $w.Write([byte]$buffer[$i + 1])
            $w.Write([byte]$buffer[$i + 2])
            $w.Write([byte]$buffer[$i + 3])
        }
    }

    # AND mask: unused for 32bpp, zero-filled and 4-byte aligned rows
    $andStride = [int]([Math]::Floor(($width + 31) / 32) * 4)
    $zeroRow = New-Object byte[] $andStride
    for ($y = 0; $y -lt $height; $y++) {
        $w.Write($zeroRow)
    }

    $w.Flush()
    $bytes = $ms.ToArray()
    $w.Dispose()
    $ms.Dispose()
    return $bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object System.Collections.ArrayList
foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    [byte[]]$dib = ConvertTo-IconDib -Bitmap $bmp
    $bmp.Dispose()
    $entry = New-Object psobject -Property @{ Size = $size; Data = $dib }
    [void]$frames.Add($entry)
}

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)

$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Data.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Data.Length
}
foreach ($frame in $frames) {
    $writer.Write([byte[]]$frame.Data)
}
$writer.Flush()
$written = $stream.ToArray()
$writer.Dispose()
$stream.Dispose()

if ($written.Length -ne $offset) {
    throw "Icon assembly mismatch: expected $offset bytes, produced $($written.Length)."
}
[System.IO.File]::WriteAllBytes($OutFile, $written)

$icon = New-Object System.Drawing.Icon($OutFile)
foreach ($size in @(16, 32, 48, 256)) {
    $sized = New-Object System.Drawing.Icon($OutFile, $size, $size)
    Write-Host ("  frame {0,3} -> {1}x{2}" -f $size, $sized.Width, $sized.Height)
    $sized.Dispose()
}
Write-Host ("icon written: {0} ({1:N0} bytes)" -f $OutFile, (Get-Item -LiteralPath $OutFile).Length)
$icon.Dispose()
