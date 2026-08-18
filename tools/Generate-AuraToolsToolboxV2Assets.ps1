param(
    [string]$OutputDirectory = "",
    [string]$ContactSheetPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "AuraToolsExp\ModResource\Images\UI\ToolboxV2"
}
if ([string]::IsNullOrWhiteSpace($ContactSheetPath)) {
    $ContactSheetPath = Join-Path $repoRoot "artifacts\AuraToolsExp\toolbox-v2-components-contact-sheet.png"
}
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $ContactSheetPath)) | Out-Null

$colors = @{
    Stage = [Drawing.ColorTranslator]::FromHtml("#070328")
    Surface = [Drawing.ColorTranslator]::FromHtml("#10143A")
    SurfaceRaised = [Drawing.ColorTranslator]::FromHtml("#181C46")
    Control = [Drawing.ColorTranslator]::FromHtml("#1B1F46")
    ControlHover = [Drawing.ColorTranslator]::FromHtml("#272B58")
    ControlPressed = [Drawing.ColorTranslator]::FromHtml("#10132F")
    Edge = [Drawing.ColorTranslator]::FromHtml("#4C4A68")
    EdgeSoft = [Drawing.ColorTranslator]::FromHtml("#333651")
    Gold = [Drawing.ColorTranslator]::FromHtml("#C2A462")
    GoldSoft = [Drawing.ColorTranslator]::FromHtml("#806F48")
    Text = [Drawing.ColorTranslator]::FromHtml("#EEE6BD")
    Check = [Drawing.ColorTranslator]::FromHtml("#777BFF")
    Disabled = [Drawing.ColorTranslator]::FromHtml("#5F6070")
}

function New-Bitmap([int]$width, [int]$height) {
    return [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

function Save-Png($bitmap, [string]$name) {
    $path = Join-Path $OutputDirectory $name
    $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    return $path
}

function New-Graphics($bitmap) {
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    return $graphics
}

function Draw-CornerNode($graphics, [int]$x, [int]$y, [int]$flipX, [int]$flipY) {
    $pen = [Drawing.Pen]::new($colors.Gold, 3)
    try {
        $graphics.DrawLine($pen, $x, $y, $x + 14 * $flipX, $y)
        $graphics.DrawLine($pen, $x, $y, $x, $y + 14 * $flipY)
        $graphics.FillRectangle([Drawing.SolidBrush]::new($colors.Gold), $x - 2, $y - 2, 5, 5)
    }
    finally {
        $pen.Dispose()
    }
}

$generated = [System.Collections.Generic.List[string]]::new()

$surface = New-Bitmap 128 128
$graphics = New-Graphics $surface
try {
    $graphics.Clear($colors.Surface)
    $graphics.DrawRectangle([Drawing.Pen]::new($colors.EdgeSoft, 2), 3, 3, 121, 121)
    $graphics.DrawRectangle([Drawing.Pen]::new([Drawing.Color]::FromArgb(90, $colors.Edge), 1), 7, 7, 113, 113)
    Draw-CornerNode $graphics 9 9 1 1
    Draw-CornerNode $graphics 118 9 -1 1
    Draw-CornerNode $graphics 9 118 1 -1
    Draw-CornerNode $graphics 118 118 -1 -1
    $generated.Add((Save-Png $surface "toolbox-surface-9slice.png"))
}
finally {
    $graphics.Dispose()
    $surface.Dispose()
}

$control = New-Bitmap 64 64
$graphics = New-Graphics $control
try {
    $graphics.Clear($colors.Control)
    $graphics.DrawRectangle([Drawing.Pen]::new($colors.Edge, 2), 2, 2, 59, 59)
    $graphics.DrawRectangle([Drawing.Pen]::new([Drawing.Color]::FromArgb(80, $colors.EdgeSoft), 1), 6, 6, 51, 51)
    $generated.Add((Save-Png $control "toolbox-control-9slice.png"))
}
finally {
    $graphics.Dispose()
    $control.Dispose()
}

$category = New-Bitmap 64 64
$graphics = New-Graphics $category
try {
    $graphics.Clear($colors.SurfaceRaised)
    $graphics.FillRectangle([Drawing.SolidBrush]::new($colors.Gold), 0, 0, 4, 64)
    $graphics.DrawLine([Drawing.Pen]::new($colors.Edge, 1), 4, 1, 63, 1)
    $graphics.DrawLine([Drawing.Pen]::new($colors.EdgeSoft, 1), 4, 62, 63, 62)
    $generated.Add((Save-Png $category "toolbox-category-selected-9slice.png"))
}
finally {
    $graphics.Dispose()
    $category.Dispose()
}

$checkbox = New-Bitmap 48 240
$graphics = New-Graphics $checkbox
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    for ($state = 0; $state -lt 5; $state++) {
        $top = $state * 48
        $fill = switch ($state) {
            2 { $colors.ControlHover }
            3 { $colors.ControlHover }
            4 { [Drawing.Color]::FromArgb(170, $colors.ControlPressed) }
            default { $colors.Control }
        }
        $edge = switch ($state) {
            1 { $colors.Gold }
            2 { $colors.Gold }
            3 { $colors.Gold }
            4 { $colors.Disabled }
            default { $colors.Edge }
        }
        $graphics.FillRectangle([Drawing.SolidBrush]::new($fill), 8, $top + 8, 32, 32)
        $graphics.DrawRectangle([Drawing.Pen]::new($edge, 2), 8, $top + 8, 31, 31)
        if ($state -eq 1 -or $state -eq 3) {
            $pen = [Drawing.Pen]::new($colors.Check, 5)
            $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
            $graphics.DrawLines($pen, @(
                [Drawing.Point]::new(14, $top + 24),
                [Drawing.Point]::new(21, $top + 31),
                [Drawing.Point]::new(35, $top + 16)))
            $pen.Dispose()
        }
    }
    $generated.Add((Save-Png $checkbox "toolbox-checkbox-atlas.png"))
}
finally {
    $graphics.Dispose()
    $checkbox.Dispose()
}

$iconButton = New-Bitmap 192 48
$graphics = New-Graphics $iconButton
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    for ($state = 0; $state -lt 4; $state++) {
        $left = $state * 48
        $fill = switch ($state) {
            1 { $colors.ControlHover }
            2 { $colors.ControlPressed }
            3 { [Drawing.Color]::FromArgb(150, $colors.Control) }
            default { $colors.Control }
        }
        $edge = switch ($state) {
            1 { $colors.Gold }
            2 { $colors.GoldSoft }
            3 { $colors.Disabled }
            default { $colors.EdgeSoft }
        }
        $graphics.FillRectangle([Drawing.SolidBrush]::new($fill), $left + 2, 2, 44, 44)
        $lineWidth = if ($state -eq 1) { 2 } else { 1 }
        $graphics.DrawRectangle([Drawing.Pen]::new($edge, $lineWidth), $left + 2, 2, 43, 43)
    }
    $generated.Add((Save-Png $iconButton "toolbox-icon-button-atlas.png"))
}
finally {
    $graphics.Dispose()
    $iconButton.Dispose()
}

$sheet = New-Bitmap 1080 1000
$graphics = New-Graphics $sheet
$titleFont = [Drawing.Font]::new("Microsoft YaHei UI", 20, [Drawing.FontStyle]::Bold)
$labelFont = [Drawing.Font]::new("Microsoft YaHei UI", 14)
$textBrush = [Drawing.SolidBrush]::new($colors.Text)
try {
    $graphics.Clear($colors.Stage)
    $graphics.DrawString("Aura Toolbox V2 Components", $titleFont, $textBrush, 28, 22)
    $scales = @(1.0, 0.75, 0.5)
    $columnX = @(28, 388, 748)
    for ($column = 0; $column -lt $scales.Count; $column++) {
        $scale = $scales[$column]
        $x = $columnX[$column]
        $graphics.DrawString(([int]($scale * 100)).ToString() + "%", $titleFont, $textBrush, $x, 72)
        $y = 118
        foreach ($asset in @(
            @{ File = "toolbox-surface-9slice.png"; Label = "Surface"; Width = 300; Height = 120 },
            @{ File = "toolbox-control-9slice.png"; Label = "Control"; Width = 260; Height = 58 },
            @{ File = "toolbox-category-selected-9slice.png"; Label = "Category selected"; Width = 260; Height = 52 },
            @{ File = "toolbox-checkbox-atlas.png"; Label = "Checkbox states"; Width = 48; Height = 240 },
            @{ File = "toolbox-icon-button-atlas.png"; Label = "Icon button states"; Width = 192; Height = 48 }
        )) {
            $graphics.DrawString($asset.Label, $labelFont, $textBrush, $x, $y)
            $image = [Drawing.Image]::FromFile((Join-Path $OutputDirectory $asset.File))
            try {
                $drawWidth = [int]($asset.Width * $scale)
                $drawHeight = [int]($asset.Height * $scale)
                $graphics.DrawImage($image, $x, $y + 26, $drawWidth, $drawHeight)
                $y += $drawHeight + 70
            }
            finally {
                $image.Dispose()
            }
        }
    }
    $sheet.Save($ContactSheetPath, [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $titleFont.Dispose()
    $labelFont.Dispose()
    $textBrush.Dispose()
    $graphics.Dispose()
    $sheet.Dispose()
}

Write-Host "Generated $($generated.Count) Aura Toolbox V2 assets in $OutputDirectory"
Write-Host "Contact sheet: $ContactSheetPath"
