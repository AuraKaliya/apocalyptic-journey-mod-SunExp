param(
    [Parameter(Mandatory = $true)]
    [string]$SourceImage,
    [string]$OutPath = "TestMods\GoldExp\ModResource\Images\CardPack\cardpack_gold_dream.png"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function New-Font {
    param(
        [string[]]$Families,
        [float]$Size,
        [System.Drawing.FontStyle]$Style = [System.Drawing.FontStyle]::Regular
    )

    foreach ($family in $Families) {
        try {
            return New-Object System.Drawing.Font -ArgumentList $family, $Size, $Style, ([System.Drawing.GraphicsUnit]::Pixel)
        }
        catch {
        }
    }

    return New-Object System.Drawing.Font -ArgumentList "Serif", $Size, $Style, ([System.Drawing.GraphicsUnit]::Pixel)
}

function Draw-CenteredText {
    param(
        [System.Drawing.Graphics]$Graphics,
        [string]$Text,
        [System.Drawing.RectangleF]$Rect,
        [System.Drawing.Font]$Font,
        [System.Drawing.Brush]$FillBrush,
        [System.Drawing.Brush]$ShadowBrush,
        [System.Drawing.Pen]$StrokePen,
        [float]$ShadowOffset = 4
    )

    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $format.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddString($Text, $Font.FontFamily, [int]$Font.Style, $Font.Size, $Rect, $format)

    $shadowPath = $path.Clone()
    $shadowMatrix = New-Object System.Drawing.Drawing2D.Matrix
    $shadowMatrix.Translate($ShadowOffset, $ShadowOffset)
    $shadowPath.Transform($shadowMatrix)

    $Graphics.FillPath($ShadowBrush, $shadowPath)
    $Graphics.DrawPath($StrokePen, $path)
    $Graphics.FillPath($FillBrush, $path)

    $shadowPath.Dispose()
    $shadowMatrix.Dispose()
    $path.Dispose()
    $format.Dispose()
}

function New-GoldDreamChineseTitle {
    return -join ([char[]]@(
        0x91D1, 0x68A6, 0xFF1A, 0x865A, 0x5047, 0x7684, 0x9EC4, 0x91D1, 0x68A6
    ))
}

$source = Resolve-Path -LiteralPath $SourceImage
$target = Join-Path (Get-Location) $OutPath
$targetDir = Split-Path -Parent $target
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

if (Test-Path -LiteralPath $target) {
    $backup = Join-Path $targetDir "cardpack_gold_dream.before-title-redesign.png"
    if (-not (Test-Path -LiteralPath $backup)) {
        Copy-Item -LiteralPath $target -Destination $backup
    }
}

$src = [System.Drawing.Image]::FromFile($source)
try {
    $bitmap = New-Object System.Drawing.Bitmap $src.Width, $src.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($src, 0, 0, $src.Width, $src.Height)

    $gold = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(253, 232, 151))
    $shadow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(215, 28, 14, 8))
    $stroke = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::FromArgb(150, 80, 43, 8)), 3
    $stroke.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $topFont = New-Font @("Georgia", "Times New Roman", "Cambria") 46 ([System.Drawing.FontStyle]::Bold)
    $bottomFont = New-Font @("STKaiti", "KaiTi", "Microsoft YaHei", "SimSun") 68 ([System.Drawing.FontStyle]::Bold)

    Draw-CenteredText `
        -Graphics $graphics `
        -Text "Gold Dream: False Gold" `
        -Rect ([System.Drawing.RectangleF]::new(150, 168, 724, 86)) `
        -Font $topFont `
        -FillBrush $gold `
        -ShadowBrush $shadow `
        -StrokePen $stroke `
        -ShadowOffset 4

    Draw-CenteredText `
        -Graphics $graphics `
        -Text (New-GoldDreamChineseTitle) `
        -Rect ([System.Drawing.RectangleF]::new(132, 1296, 760, 94)) `
        -Font $bottomFont `
        -FillBrush $gold `
        -ShadowBrush $shadow `
        -StrokePen $stroke `
        -ShadowOffset 5

    $graphics.Dispose()
    $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()

    $topFont.Dispose()
    $bottomFont.Dispose()
    $gold.Dispose()
    $shadow.Dispose()
    $stroke.Dispose()
}
finally {
    $src.Dispose()
}

Write-Host "Saved titled card pack cover: $target"
