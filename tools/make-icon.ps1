# Generates src\PortaSnap\Assets\PortaSnap.ico (16-256 px) from code, so the project has no binary art dependencies.
# Small sizes are stored as 32-bit DIBs (widest compatibility), 256 px as PNG.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outPath = Join-Path $root "src\PortaSnap\Assets\PortaSnap.ico"
New-Item -ItemType Directory -Force (Split-Path $outPath) | Out-Null

function New-IconBitmap([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
  $s = [single]$size; $pad = [single]([math]::Max(1, $size * 0.04)); $r = [single]($size * 0.22)
  $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
  $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
  $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
  $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
  $path.CloseFigure()
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 37, 99, 235)), ([System.Drawing.Color]::FromArgb(255, 147, 51, 234)), 45.0
  $g.FillPath($brush, $path)
  # Viewfinder corners
  $w = [single]([math]::Max(1.5, $size * 0.075))
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $w
  $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
  $a = $s * 0.24; $b = $s * 0.76; $l = $s * 0.17
  $g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $a, ($a + $l)), (New-Object System.Drawing.PointF $a, $a), (New-Object System.Drawing.PointF ($a + $l), $a)))
  $g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ($b - $l), $a), (New-Object System.Drawing.PointF $b, $a), (New-Object System.Drawing.PointF $b, ($a + $l))))
  $g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $b, ($b - $l)), (New-Object System.Drawing.PointF $b, $b), (New-Object System.Drawing.PointF ($b - $l), $b)))
  $g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ($a + $l), $b), (New-Object System.Drawing.PointF $a, $b), (New-Object System.Drawing.PointF $a, ($b - $l))))
  # Record dot
  $dot = $s * 0.2
  $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 239, 68, 68))), ($s - $dot) / 2, ($s - $dot) / 2, $dot, $dot)
  $g.Dispose()
  return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
  $size = $bmp.Width
  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter $ms
  $bw.Write([int]40); $bw.Write([int]$size); $bw.Write([int]($size * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
  $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
  for ($y = $size - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $size; $x++) { $c = $bmp.GetPixel($x, $y); $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A) } }
  $maskStride = [int]([math]::Ceiling($size / 32.0) * 4)
  $bw.Write((New-Object byte[] ($maskStride * $size)))
  $bw.Flush(); return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($sz in $sizes) {
  $bmp = New-IconBitmap $sz
  if ($sz -eq 256) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); [byte[]]$bytes = $ms.ToArray() } else { [byte[]]$bytes = Get-DibBytes $bmp }
  $bmp.Dispose()
  [pscustomobject]@{ Size = $sz; Bytes = $bytes }
}

$fs = [System.IO.File]::Create($outPath)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
  $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$img.Bytes.Length); $w.Write([int]$offset)
  $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img.Bytes) }
$w.Close()
Write-Host "Wrote $outPath" -ForegroundColor Green
