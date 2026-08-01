param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\SteelSeriesAssist.App\Assets\steelseries-assist.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconPngBytes([int]$Size) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $scale = $Size / 32.0

        $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 23, 31, 36))
        $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 103, 117, 255), 1.8 * $scale)
        try {
            $graphics.FillEllipse($background, 1.5 * $scale, 1.5 * $scale, 29 * $scale, 29 * $scale)
            $graphics.DrawEllipse($outline, 2.4 * $scale, 2.4 * $scale, 27.2 * $scale, 27.2 * $scale)
        }
        finally {
            $background.Dispose()
            $outline.Dispose()
        }

        $faders = @(
            @{ X = 9;  Y = 13; Color = [System.Drawing.Color]::FromArgb(255, 103, 117, 255) },
            @{ X = 16; Y = 20; Color = [System.Drawing.Color]::FromArgb(255, 45, 177, 252) },
            @{ X = 23; Y = 10; Color = [System.Drawing.Color]::FromArgb(255, 2, 221, 188) }
        )
        foreach ($fader in $faders) {
            $rail = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(220, 210, 219, 230), 1.6 * $scale)
            $rail.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $rail.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $knob = [System.Drawing.SolidBrush]::new($fader.Color)
            $knobOutline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 23, 31, 36), 1.1 * $scale)
            try {
                $graphics.DrawLine($rail, $fader.X * $scale, 7 * $scale, $fader.X * $scale, 25 * $scale)
                $graphics.FillEllipse($knob, ($fader.X - 3.1) * $scale, ($fader.Y - 3.1) * $scale, 6.2 * $scale, 6.2 * $scale)
                $graphics.DrawEllipse($knobOutline, ($fader.X - 3.1) * $scale, ($fader.Y - 3.1) * $scale, 6.2 * $scale, 6.2 * $scale)
            }
            finally {
                $rail.Dispose()
                $knob.Dispose()
                $knobOutline.Dispose()
            }
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$sizes = @(16, 20, 24, 32, 48, 64, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    [byte[]]$pngBytes = @(New-IconPngBytes $size)
    $images.Add($pngBytes)
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$file = [System.IO.File]::Create($resolvedOutput)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $sizeByte = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) {
        $writer.Write($image)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output $resolvedOutput
