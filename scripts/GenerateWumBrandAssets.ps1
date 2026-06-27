Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath {
    param(
        [System.Drawing.RectangleF]$Rect,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Rect.X, $Rect.Y, $Radius, $Radius, 180, 90)
    $path.AddArc($Rect.Right - $Radius, $Rect.Y, $Radius, $Radius, 270, 90)
    $path.AddArc($Rect.Right - $Radius, $Rect.Bottom - $Radius, $Radius, $Radius, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $Radius, $Radius, $Radius, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-WumBitmap {
    param([int]$Size)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform(($Size / 180.0), ($Size / 180.0))

    $rect = [System.Drawing.RectangleF]::new(18, 18, 144, 144)
    $shell = New-RoundedRectPath -Rect $rect -Radius 34

    $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $rect,
        [System.Drawing.Color]::FromArgb(8, 17, 31),
        [System.Drawing.Color]::FromArgb(109, 53, 243),
        45
    )
    $blend = [System.Drawing.Drawing2D.ColorBlend]::new(3)
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(8, 17, 31),
        [System.Drawing.Color]::FromArgb(18, 60, 131),
        [System.Drawing.Color]::FromArgb(109, 53, 243)
    )
    $blend.Positions = @(0.0, 0.48, 1.0)
    $bg.InterpolationColors = $blend
    $g.FillPath($bg, $shell)
    $bg.Dispose()
    $shell.Dispose()

    $borderPath = New-RoundedRectPath -Rect ([System.Drawing.RectangleF]::new(21.5, 21.5, 137, 137)) -Radius 30
    $border = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(205, 98, 244, 215), 3)
    $g.DrawPath($border, $borderPath)
    $border.Dispose()
    $borderPath.Dispose()

    $grid = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(20, 255, 255, 255), 2)
    foreach ($y in @(51, 90, 129)) {
        $g.DrawLine($grid, 39, $y, 143, $y)
    }
    foreach ($x in @(58, 97, 136)) {
        $g.DrawLine($grid, $x, 36, $x, 144)
    }
    $grid.Dispose()

    $mint = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(98, 244, 215), 8)
    $mint.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $mint.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $mint.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawArc($mint, 47, 44, 92, 92, -44, 285)
    $g.DrawLine($mint, 123, 56, 121, 78)
    $g.DrawLine($mint, 123, 56, 140, 67)
    $mint.Dispose()

    $w = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(248, 251, 255), 13)
    $w.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $w.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $w.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($w, @(
        [System.Drawing.PointF]::new(44, 67),
        [System.Drawing.PointF]::new(61, 124),
        [System.Drawing.PointF]::new(87, 73),
        [System.Drawing.PointF]::new(112, 124),
        [System.Drawing.PointF]::new(136, 67)
    ))
    $w.Dispose()

    $cursor = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(235, 248, 251, 255), 8)
    $cursor.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cursor.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($cursor, 51, 141, 123, 141)
    $cursor.Dispose()

    $prompt = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(98, 244, 215), 7)
    $prompt.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $prompt.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $prompt.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($prompt, @(
        [System.Drawing.PointF]::new(49, 122),
        [System.Drawing.PointF]::new(63, 135),
        [System.Drawing.PointF]::new(49, 148)
    ))
    $prompt.Dispose()

    $g.Dispose()
    return $bmp
}

function Save-Png {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [string]$Path
    )

    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function Save-Ico {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $pngs = @()
    foreach ($size in $Sizes) {
        $bitmap = New-WumBitmap -Size $size
        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += [pscustomobject]@{
            Size = $size
            Bytes = $stream.ToArray()
        }
        $stream.Dispose()
        $bitmap.Dispose()
    }

    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$pngs.Count)

        $offset = 6 + (16 * $pngs.Count)
        foreach ($png in $pngs) {
            $dim = if ($png.Size -ge 256) { 0 } else { $png.Size }
            $writer.Write([byte]$dim)
            $writer.Write([byte]$dim)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$png.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $png.Bytes.Length
        }

        foreach ($png in $pngs) {
            $writer.Write($png.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

function New-SocialPng {
    param([string]$Path)

    $bmp = [System.Drawing.Bitmap]::new(1200, 630, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(5, 11, 20))

    $bgRect = [System.Drawing.RectangleF]::new(0, 0, 1200, 630)
    $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $bgRect,
        [System.Drawing.Color]::FromArgb(5, 11, 20),
        [System.Drawing.Color]::FromArgb(33, 22, 74),
        30
    )
    $g.FillRectangle($bg, $bgRect)
    $bg.Dispose()

    $grid = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(42, 98, 244, 215), 1)
    for ($y = 85; $y -le 485; $y += 80) {
        $g.DrawLine($grid, 80, $y, 1120, $y)
    }
    for ($x = 120; $x -le 1080; $x += 120) {
        $g.DrawLine($grid, $x, 55, $x, 575)
    }
    $grid.Dispose()

    $mark = New-WumBitmap -Size 198
    $g.DrawImage($mark, 172, 120, 198, 198)
    $mark.Dispose()

    $titleFont = [System.Drawing.Font]::new('Segoe UI', 106, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $subFont = [System.Drawing.Font]::new('Segoe UI', 40, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $bodyFont = [System.Drawing.Font]::new('Segoe UI', 28, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $smallFont = [System.Drawing.Font]::new('Segoe UI', 24, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $codeFont = [System.Drawing.Font]::new('Consolas', 25, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)

    $titleBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(438, 150, 340, 120),
        [System.Drawing.Color]::FromArgb(98, 244, 215),
        [System.Drawing.Color]::FromArgb(182, 155, 255),
        0
    )
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(248, 251, 255))
    $body = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(199, 210, 229))
    $muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(148, 163, 184))
    $codeText = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(220, 235, 255))

    $g.DrawString('WUM', $titleFont, $titleBrush, 438, 146)
    $g.DrawString('Windows Update Manager CLI', $subFont, $white, 444, 266)
    $g.DrawString('List, install, pause, schedule, diagnose.', $bodyFont, $body, 446, 339)
    $g.DrawString('Scriptable Windows updates powered by .NET 10.', $smallFont, $muted, 446, 393)

    $codeRect = [System.Drawing.RectangleF]::new(446, 474, 398, 58)
    $codePath = New-RoundedRectPath -Rect $codeRect -Radius 13
    $codeBg = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(5, 11, 20))
    $codeBorder = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(45, 68, 101), 1)
    $g.FillPath($codeBg, $codePath)
    $g.DrawPath($codeBorder, $codePath)
    $g.DrawString('wum status --json', $codeFont, $codeText, 471, 489)

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    foreach ($item in @($codeBorder, $codeBg, $codePath, $codeText, $white, $body, $muted, $titleBrush, $titleFont, $subFont, $bodyFont, $smallFont, $codeFont)) {
        if ($null -ne $item) {
            $item.Dispose()
        }
    }
    $g.Dispose()
    $bmp.Dispose()
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$docs = Join-Path $root 'docs'
$cli = Join-Path $root 'src\WUM.CLI'

$apple = New-WumBitmap -Size 256
Save-Png -Bitmap $apple -Path (Join-Path $docs 'apple-touch-icon.png')
$apple.Dispose()

Save-Ico -Path (Join-Path $docs 'favicon.ico') -Sizes @(16, 24, 32, 48, 64, 128, 256)
Save-Ico -Path (Join-Path $cli 'wum.ico') -Sizes @(16, 24, 32, 48, 64, 128, 256)
New-SocialPng -Path (Join-Path $docs 'assets\wum-social.png')

Write-Host 'Generated WUM brand PNG/ICO assets.'
