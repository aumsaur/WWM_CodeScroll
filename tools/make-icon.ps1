# Renders the AppIcon drawing (src\WwmRedeem\Assets\AppIcon.xaml) into
# Assets\app.ico: 16-48 px as 32-bit bitmaps, which the tray and Explorer use
# for small sizes, and 256 px as PNG. -Preview also saves a 256 px PNG there.
param([string]$Preview)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$assets = Join-Path $PSScriptRoot '..\src\WwmRedeem\Assets'
$stream = [IO.File]::OpenRead((Join-Path $assets 'AppIcon.xaml'))
$icon = ([Windows.Markup.XamlReader]::Load($stream))['AppIcon']
$stream.Close()

function Render([int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.DrawImage($icon, (New-Object Windows.Rect -ArgumentList 0, 0, $size, $size))
    $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap -ArgumentList $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $bmp
}

function PngBytes($bmp) {
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream
    $encoder.Save($ms)
    return , $ms.ToArray()
}

function DibBytes($bmp, [int]$size) {
    # ICO bitmaps are bottom-up BGRA with straight alpha, then a 1-bpp AND mask.
    $straight = New-Object Windows.Media.Imaging.FormatConvertedBitmap -ArgumentList $bmp, ([Windows.Media.PixelFormats]::Bgra32), $null, 0.0
    $stride = $size * 4
    $pixels = New-Object byte[] ($stride * $size)
    $straight.CopyPixels($pixels, $stride, 0)
    $maskStride = [int][Math]::Ceiling($size / 32.0) * 4
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($stride * $size)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $stride, $stride) }
    $w.Write((New-Object byte[] ($maskStride * $size)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 256
$images = New-Object 'Collections.Generic.List[byte[]]'
foreach ($s in $sizes) {
    $bmp = Render $s
    if ($s -eq 256) { $images.Add((PngBytes $bmp)) } else { $images.Add((DibBytes $bmp $s)) }
}

$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256 in an ICO directory
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $out.ToArray())
Write-Host "Wrote $(Join-Path $assets 'app.ico') ($($out.Length) bytes)"

if ($Preview) { [IO.File]::WriteAllBytes($Preview, (PngBytes (Render 256))) }
