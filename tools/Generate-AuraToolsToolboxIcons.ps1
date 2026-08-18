param(
    [string]$OutputDirectory = "",
    [string]$ContactSheetPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "AuraToolsExp\ModResource\Images\UI\ToolboxIcons"
}
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

$iconNames = @(
    "all", "gameplay", "presentation", "records", "multiplayer", "intelligence", "system", "extensions",
    "file-logging", "skin", "battle-bgm", "card-use-audio", "starter-deck", "card-refresh", "feast", "safe-box",
    "pixel-emoji", "mod-sync", "damage-statistics", "battle-replay", "auto-battle", "skill-cg", "card-use-cg",
    "search", "clear", "folder", "settings", "warning"
)

function New-IconPen([float]$width = 4) {
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function Draw-RoundedRect($graphics, $pen, [float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $diameter = $radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
        $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
        $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()
        $graphics.DrawPath($pen, $path)
    }
    finally {
        $path.Dispose()
    }
}

function Fill-RoundedRect($graphics, $brush, [float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $diameter = $radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
        $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
        $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()
        $graphics.FillPath($brush, $path)
    }
    finally {
        $path.Dispose()
    }
}

function Draw-Arrow($graphics, $pen, [float]$x1, [float]$y1, [float]$x2, [float]$y2, [bool]$reverse = $false) {
    $graphics.DrawLine($pen, $x1, $y1, $x2, $y2)
    if ($reverse) {
        $graphics.DrawLine($pen, $x1, $y1, $x1 + 7, $y1 - 6)
        $graphics.DrawLine($pen, $x1, $y1, $x1 + 7, $y1 + 6)
    }
    else {
        $graphics.DrawLine($pen, $x2, $y2, $x2 - 7, $y2 - 6)
        $graphics.DrawLine($pen, $x2, $y2, $x2 - 7, $y2 + 6)
    }
}

function Draw-Gear($graphics, $pen) {
    $graphics.DrawEllipse($pen, 20, 20, 24, 24)
    $graphics.DrawEllipse($pen, 28, 28, 8, 8)
    for ($i = 0; $i -lt 8; $i++) {
        $angle = $i * [Math]::PI / 4
        $x1 = 32 + [Math]::Cos($angle) * 15
        $y1 = 32 + [Math]::Sin($angle) * 15
        $x2 = 32 + [Math]::Cos($angle) * 22
        $y2 = 32 + [Math]::Sin($angle) * 22
        $graphics.DrawLine($pen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)
    }
}

function Draw-Document($graphics, $pen) {
    $graphics.DrawLine($pen, 18, 10, 40, 10)
    $graphics.DrawLine($pen, 40, 10, 50, 20)
    $graphics.DrawLine($pen, 50, 20, 50, 54)
    $graphics.DrawLine($pen, 50, 54, 18, 54)
    $graphics.DrawLine($pen, 18, 54, 18, 10)
    $graphics.DrawLine($pen, 40, 10, 40, 21)
    $graphics.DrawLine($pen, 40, 21, 50, 21)
    $graphics.DrawLine($pen, 25, 31, 43, 31)
    $graphics.DrawLine($pen, 25, 40, 43, 40)
}

function Draw-Card($graphics, $pen, [int]$offsetX = 0, [int]$offsetY = 0) {
    Draw-RoundedRect $graphics $pen (18 + $offsetX) (12 + $offsetY) 30 40 4
    $graphics.DrawLine($pen, 25 + $offsetX, 22 + $offsetY, 41 + $offsetX, 22 + $offsetY)
}

function Draw-Sparkle($graphics, $pen, [float]$cx, [float]$cy, [float]$radius) {
    $graphics.DrawLine($pen, $cx, $cy - $radius, $cx, $cy + $radius)
    $graphics.DrawLine($pen, $cx - $radius, $cy, $cx + $radius, $cy)
    $graphics.DrawLine($pen, $cx - $radius * 0.65, $cy - $radius * 0.65, $cx + $radius * 0.65, $cy + $radius * 0.65)
    $graphics.DrawLine($pen, $cx + $radius * 0.65, $cy - $radius * 0.65, $cx - $radius * 0.65, $cy + $radius * 0.65)
}

function Draw-Icon($graphics, [string]$name) {
    $pen = New-IconPen
    $thin = New-IconPen 3
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    try {
        switch ($name) {
            "all" {
                foreach ($x in @(14, 36)) { foreach ($y in @(14, 36)) { Draw-RoundedRect $graphics $pen $x $y 14 14 2 } }
            }
            "gameplay" {
                Draw-RoundedRect $graphics $pen 9 20 46 28 9
                $graphics.DrawLine($pen, 20, 29, 20, 40); $graphics.DrawLine($pen, 15, 34, 25, 34)
                $graphics.FillEllipse($brush, 40, 28, 6, 6); $graphics.FillEllipse($brush, 47, 35, 6, 6)
            }
            "presentation" {
                $graphics.DrawEllipse($pen, 9, 20, 46, 25); $graphics.DrawEllipse($pen, 26, 26, 12, 12)
                Draw-Sparkle $graphics $thin 48 16 7
            }
            "records" {
                $graphics.DrawEllipse($pen, 11, 11, 42, 42)
                $graphics.DrawLine($pen, 32, 20, 32, 34); $graphics.DrawLine($pen, 32, 34, 42, 40)
            }
            "multiplayer" {
                $graphics.DrawEllipse($pen, 12, 13, 14, 14); $graphics.DrawEllipse($pen, 38, 13, 14, 14)
                $graphics.DrawArc($pen, 7, 29, 24, 23, 190, 160); $graphics.DrawArc($pen, 33, 29, 24, 23, 190, 160)
                $graphics.DrawLine($thin, 26, 35, 38, 35)
            }
            "intelligence" {
                $points = @([Drawing.PointF]::new(32, 8), [Drawing.PointF]::new(51, 19), [Drawing.PointF]::new(51, 43), [Drawing.PointF]::new(32, 55), [Drawing.PointF]::new(13, 43), [Drawing.PointF]::new(13, 19))
                $graphics.DrawPolygon($pen, $points); $graphics.DrawEllipse($thin, 26, 26, 12, 12)
                $graphics.DrawLine($thin, 32, 14, 32, 26); $graphics.DrawLine($thin, 20, 22, 27, 28); $graphics.DrawLine($thin, 44, 22, 37, 28)
            }
            "system" { Draw-Gear $graphics $pen }
            "extensions" {
                $graphics.DrawRectangle($pen, 15, 15, 34, 34)
                $graphics.DrawArc($pen, 25, 7, 14, 16, 0, 180); $graphics.DrawArc($pen, 41, 25, 16, 14, 90, 180)
                $graphics.DrawArc($pen, 25, 41, 14, 16, 180, 180); $graphics.DrawArc($pen, 7, 25, 16, 14, 270, 180)
            }
            "file-logging" { Draw-Document $graphics $pen }
            "skin" {
                $graphics.DrawEllipse($pen, 23, 10, 18, 18); $graphics.DrawArc($pen, 13, 30, 38, 27, 185, 170)
                Draw-Sparkle $graphics $thin 49 18 6
            }
            "battle-bgm" {
                $graphics.DrawLine($pen, 28, 14, 48, 10); $graphics.DrawLine($pen, 28, 14, 28, 44); $graphics.DrawLine($pen, 48, 10, 48, 39)
                $graphics.FillEllipse($brush, 17, 39, 13, 10); $graphics.FillEllipse($brush, 37, 34, 13, 10)
            }
            "card-use-audio" {
                $graphics.DrawPolygon($pen, @([Drawing.PointF]::new(12, 27), [Drawing.PointF]::new(22, 27), [Drawing.PointF]::new(34, 17), [Drawing.PointF]::new(34, 47), [Drawing.PointF]::new(22, 37), [Drawing.PointF]::new(12, 37)))
                $graphics.DrawArc($pen, 32, 21, 18, 22, -55, 110); $graphics.DrawArc($thin, 35, 15, 23, 34, -55, 110)
            }
            "starter-deck" {
                Draw-RoundedRect $graphics $thin 11 18 27 35 3; Draw-RoundedRect $graphics $thin 18 13 27 35 3; Draw-RoundedRect $graphics $pen 25 9 27 35 3
                $graphics.DrawLine($thin, 32, 20, 45, 20)
            }
            "card-refresh" {
                Draw-Card $graphics $thin 0 0
                $graphics.DrawArc($pen, 6, 19, 32, 32, 115, 190); $graphics.DrawLine($pen, 10, 20, 8, 31); $graphics.DrawLine($pen, 10, 20, 21, 20)
            }
            "feast" {
                $graphics.DrawArc($pen, 12, 24, 40, 28, 0, 180); $graphics.DrawLine($pen, 14, 38, 50, 38); $graphics.DrawLine($pen, 22, 49, 42, 49)
                $graphics.DrawLine($thin, 24, 13, 24, 27); $graphics.DrawLine($thin, 32, 10, 32, 27); $graphics.DrawLine($thin, 40, 13, 40, 27)
            }
            "safe-box" {
                Draw-RoundedRect $graphics $pen 10 13 44 40 5; $graphics.DrawEllipse($pen, 23, 23, 18, 18)
                $graphics.DrawLine($thin, 32, 26, 32, 38); $graphics.DrawLine($thin, 26, 32, 38, 32)
            }
            "pixel-emoji" {
                $graphics.DrawEllipse($pen, 10, 10, 44, 44); $graphics.FillEllipse($brush, 22, 25, 5, 5); $graphics.FillEllipse($brush, 38, 25, 5, 5)
                $graphics.DrawArc($pen, 21, 28, 23, 16, 15, 150)
            }
            "mod-sync" {
                Draw-Arrow $graphics $pen 13 23 49 23; Draw-Arrow $graphics $pen 51 41 15 41 $true
            }
            "damage-statistics" {
                $graphics.DrawLine($pen, 12, 52, 52, 52); $graphics.DrawLine($pen, 12, 52, 12, 12)
                $graphics.DrawLine($pen, 18, 43, 27, 33); $graphics.DrawLine($pen, 27, 33, 35, 39); $graphics.DrawLine($pen, 35, 39, 49, 20)
            }
            "battle-replay" {
                $graphics.DrawEllipse($pen, 10, 10, 44, 44)
                $graphics.DrawPolygon($pen, @([Drawing.PointF]::new(27, 21), [Drawing.PointF]::new(27, 43), [Drawing.PointF]::new(44, 32)))
            }
            "auto-battle" {
                Draw-Gear $graphics $thin
                $graphics.DrawPolygon($pen, @([Drawing.PointF]::new(29, 23), [Drawing.PointF]::new(29, 41), [Drawing.PointF]::new(43, 32)))
            }
            "skill-cg" {
                Draw-RoundedRect $graphics $pen 10 14 44 36 5
                Draw-Sparkle $graphics $thin 32 32 11
            }
            "card-use-cg" {
                Draw-Card $graphics $pen -4 0
                $graphics.DrawPolygon($thin, @([Drawing.PointF]::new(34, 28), [Drawing.PointF]::new(34, 42), [Drawing.PointF]::new(46, 35)))
            }
            "search" {
                $graphics.DrawEllipse($pen, 11, 11, 30, 30); $graphics.DrawLine($pen, 39, 39, 53, 53)
            }
            "clear" {
                $graphics.DrawLine($pen, 17, 17, 47, 47); $graphics.DrawLine($pen, 47, 17, 17, 47)
            }
            "folder" {
                $graphics.DrawLine($pen, 9, 20, 27, 20); $graphics.DrawLine($pen, 27, 20, 32, 25); $graphics.DrawLine($pen, 32, 25, 55, 25)
                Draw-RoundedRect $graphics $pen 9 20 46 33 4
            }
            "settings" { Draw-Gear $graphics $pen }
            "warning" {
                $graphics.DrawPolygon($pen, @([Drawing.PointF]::new(32, 9), [Drawing.PointF]::new(56, 52), [Drawing.PointF]::new(8, 52)))
                $graphics.DrawLine($pen, 32, 24, 32, 38); $graphics.FillEllipse($brush, 29.5, 43, 5, 5)
            }
        }
    }
    finally {
        $pen.Dispose()
        $thin.Dispose()
        $brush.Dispose()
    }
}

$generated = @()
foreach ($iconName in $iconNames) {
    $bitmap = [System.Drawing.Bitmap]::new(64, 64, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        Draw-Icon $graphics $iconName
        $path = Join-Path $OutputDirectory ($iconName + ".png")
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $generated += $path
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$trackBitmap = [System.Drawing.Bitmap]::new(64, 36, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$trackGraphics = [System.Drawing.Graphics]::FromImage($trackBitmap)
$controlBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
try {
    $trackGraphics.Clear([System.Drawing.Color]::Transparent)
    $trackGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    Fill-RoundedRect $trackGraphics $controlBrush 1 1 62 34 17
    $trackPath = Join-Path $OutputDirectory "switch-track.png"
    $trackBitmap.Save($trackPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $generated += $trackPath
}
finally {
    $trackGraphics.Dispose()
    $trackBitmap.Dispose()
}

$thumbBitmap = [System.Drawing.Bitmap]::new(36, 36, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$thumbGraphics = [System.Drawing.Graphics]::FromImage($thumbBitmap)
try {
    $thumbGraphics.Clear([System.Drawing.Color]::Transparent)
    $thumbGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $thumbGraphics.FillEllipse($controlBrush, 1, 1, 34, 34)
    $thumbPath = Join-Path $OutputDirectory "switch-thumb.png"
    $thumbBitmap.Save($thumbPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $generated += $thumbPath
}
finally {
    $thumbGraphics.Dispose()
    $thumbBitmap.Dispose()
    $controlBrush.Dispose()
}

if (-not [string]::IsNullOrWhiteSpace($ContactSheetPath)) {
    $columns = 5
    $cellWidth = 128
    $cellHeight = 96
    $rows = [Math]::Ceiling($iconNames.Count / $columns)
    $sheet = [System.Drawing.Bitmap]::new($columns * $cellWidth, $rows * $cellHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($sheet)
    $labelFont = [System.Drawing.Font]::new("Arial", 9)
    $labelBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(235, 230, 220))
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(18, 17, 25))
        for ($index = 0; $index -lt $iconNames.Count; $index++) {
            $x = ($index % $columns) * $cellWidth
            $y = [Math]::Floor($index / $columns) * $cellHeight
            $icon = [System.Drawing.Image]::FromFile($generated[$index])
            try {
                $graphics.DrawImage($icon, $x + 16, $y + 6, 64, 64)
            }
            finally {
                $icon.Dispose()
            }
            $graphics.DrawString($iconNames[$index], $labelFont, $labelBrush, $x + 4, $y + 73)
        }
        $contactParent = Split-Path -Parent $ContactSheetPath
        if (-not [string]::IsNullOrWhiteSpace($contactParent)) {
            [System.IO.Directory]::CreateDirectory($contactParent) | Out-Null
        }
        $sheet.Save($ContactSheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $labelFont.Dispose()
        $labelBrush.Dispose()
        $graphics.Dispose()
        $sheet.Dispose()
    }
}

Write-Host "Generated $($generated.Count) AuraTools toolbox icons in $OutputDirectory"
