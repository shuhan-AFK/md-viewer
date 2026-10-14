param(
    [Parameter(Mandatory)] [string] $PngPath,
    [Parameter(Mandatory)] [string] $IcoPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::FromArgb(245, 245, 245))

$whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$foldBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(229, 229, 229))
$textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(51, 51, 51))
$borderPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(102, 102, 102), 8)
$font = [System.Drawing.Font]::new('Arial', 48, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$format = [System.Drawing.StringFormat]::new()
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center

$page = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(56, 30),
    [System.Drawing.PointF]::new(159, 30),
    [System.Drawing.PointF]::new(200, 71),
    [System.Drawing.PointF]::new(200, 226),
    [System.Drawing.PointF]::new(56, 226)
)
$graphics.FillPolygon($whiteBrush, $page)
$graphics.DrawPolygon($borderPen, $page)
$fold = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(159, 30),
    [System.Drawing.PointF]::new(159, 71),
    [System.Drawing.PointF]::new(200, 71)
)
$graphics.FillPolygon($foldBrush, $fold)
$graphics.DrawLines($borderPen, $fold)
$graphics.DrawString('MD', $font, $textBrush, [System.Drawing.RectangleF]::new(62, 94, 132, 92), $format)

$pngDirectory = Split-Path -Parent $PngPath
$icoDirectory = Split-Path -Parent $IcoPath
New-Item -ItemType Directory -Path $pngDirectory, $icoDirectory -Force | Out-Null
$bitmap.Save($PngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$format.Dispose()
$font.Dispose()
$borderPen.Dispose()
$textBrush.Dispose()
$foldBrush.Dispose()
$whiteBrush.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

$pngBytes = [System.IO.File]::ReadAllBytes($PngPath)
$stream = [System.IO.File]::Create($IcoPath)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]1)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]32)
$writer.Write([UInt32]$pngBytes.Length)
$writer.Write([UInt32]22)
$writer.Write($pngBytes)
$writer.Dispose()
$stream.Dispose()

Write-Output "Created $PngPath and $IcoPath"
