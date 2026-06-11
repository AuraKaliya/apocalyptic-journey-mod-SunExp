Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$repo = Split-Path -Parent $PSScriptRoot
$roleDir = Join-Path $repo "SunExp\ModResource\Images\Role\WuNa"
$previewDir = Join-Path $repo "tools\previews\wuna_skill_icons"
$sourcePath = Join-Path $previewDir "wuna_skill_style_source.png"

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Missing source image: $sourcePath"
}

New-Item -ItemType Directory -Force -Path $roleDir, $previewDir | Out-Null

function Save-CroppedIcon {
    param(
        [System.Drawing.Image] $Source,
        [string] $Name,
        [int] $X,
        [int] $Y
    )

    $crop = New-Object System.Drawing.Rectangle $X, $Y, 625, 625
    $bmp = New-Object System.Drawing.Bitmap 384, 384, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Black)

    $dest = New-Object System.Drawing.Rectangle 0, 0, 384, 384
    $g.DrawImage($Source, $dest, $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()

    $path = Join-Path $roleDir $Name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    return $path
}

function New-ContactSheet {
    param([string[]] $Files)

    $sheet = New-Object System.Drawing.Bitmap 848, 456, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($sheet)
    $sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.Clear([System.Drawing.ColorTranslator]::FromHtml("#090909"))

    for ($i = 0; $i -lt $Files.Count; $i++) {
        $icon = [System.Drawing.Image]::FromFile($Files[$i])
        $x = 24 + (($i % 4) * 206)
        $sg.DrawImage($icon, $x, 24, 176, 176)
        $sg.DrawImage($icon, $x, 230, 64, 64)
        $sg.DrawImage($icon, $x + 88, 230, 32, 32)
        $icon.Dispose()
    }

    $sheetPath = Join-Path $previewDir "contact_sheet.png"
    $sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $sg.Dispose()
    $sheet.Dispose()

    return $sheetPath
}

$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    $files = @(
        Save-CroppedIcon $source "passive_solar_witch.png" 0 0
        Save-CroppedIcon $source "passive_ash_rebirth.png" 629 0
        Save-CroppedIcon $source "action_white_sun_prayer.png" 0 629
        Save-CroppedIcon $source "action_grave_song.png" 629 629
    )

    $contact = New-ContactSheet $files
    Write-Host "Generated WuNa skill icons:"
    $files | ForEach-Object { Write-Host " - $_" }
    Write-Host "Preview: $contact"
}
finally {
    $source.Dispose()
}
