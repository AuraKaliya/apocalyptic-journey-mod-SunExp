param(
    [string]$ReferenceImage = "C:\Users\Administrator\.codex\generated_images\019ec06c-b6e3-76f3-bc6a-10dc5c1f0555\ig_00fdedc65fe45ac8016a2da98c93a881938859cfb9e9d05bcf.png",
    [switch]$RegenerateProceduralCardIcons
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$modRoot = Join-Path $repoRoot "GoldExp"
$bright = [System.Drawing.Color]::FromArgb(253, 251, 200)
$darkGold = [System.Drawing.Color]::FromArgb(229, 179, 64)
$accent = [System.Drawing.Color]::FromArgb(171, 244, 156)
$bg = [System.Drawing.Color]::FromArgb(4, 2, 48)
$ink = [System.Drawing.Color]::FromArgb(10, 6, 24)

function Save-Bitmap {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [string]$Path
    )

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function New-Canvas {
    param([int]$Width, [int]$Height)

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear($bg)
    return @($bitmap, $graphics)
}

function New-Pen {
    param([System.Drawing.Color]$Color, [float]$Width)
    $pen = New-Object System.Drawing.Pen $Color, $Width
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    return $pen
}

function Add-BrushStrokes {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$Width,
        [int]$Height,
        [int]$Seed
    )

    $random = New-Object System.Random $Seed
    for ($i = 0; $i -lt 22; $i++) {
        $alpha = 32 + $random.Next(48)
        $mutedGold = [System.Drawing.Color]::FromArgb(120, 126, 90, 28)
        $mutedGreen = [System.Drawing.Color]::FromArgb(110, 96, 154, 118)
        $mutedBlue = [System.Drawing.Color]::FromArgb(105, 118, 135, 170)
        $color = if ($i % 3 -eq 0) { [System.Drawing.Color]::FromArgb($alpha, $mutedGold) } elseif ($i % 3 -eq 1) { [System.Drawing.Color]::FromArgb($alpha, $mutedGreen) } else { [System.Drawing.Color]::FromArgb($alpha, $mutedBlue) }
        $pen = New-Pen $color (2 + $random.Next(5))
        $x1 = $random.Next($Width)
        $y1 = $random.Next($Height)
        $x2 = [Math]::Max(0, [Math]::Min($Width, $x1 + $random.Next(-90, 91)))
        $y2 = [Math]::Max(0, [Math]::Min($Height, $y1 + $random.Next(-90, 91)))
        $Graphics.DrawLine($pen, $x1, $y1, $x2, $y2)
        $pen.Dispose()
    }
}

function Add-BrushCurve {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Point[]]$Points,
        [System.Drawing.Color]$Color,
        [float]$Width
    )

    $shadow = New-Pen $ink ($Width + 10)
    $shadow.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $main = New-Pen $Color $Width
    $main.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $shine = New-Pen ([System.Drawing.Color]::FromArgb(165, $bright)) ([Math]::Max(2, $Width * 0.32))
    $shine.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $Graphics.DrawCurve($shadow, $Points, 0.42)
    $Graphics.DrawCurve($main, $Points, 0.42)
    $Graphics.DrawCurve($shine, $Points, 0.42)
    $shadow.Dispose()
    $main.Dispose()
    $shine.Dispose()
}

function Add-Splatter {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$Width,
        [int]$Height,
        [int]$Seed,
        [int]$Count = 28
    )

    $random = New-Object System.Random $Seed
    $brushes = @(
        (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(160, $darkGold))),
        (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(130, $bright))),
        (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(105, $accent)))
    )
    for ($i = 0; $i -lt $Count; $i++) {
        $size = 3 + $random.Next(12)
        $x = $random.Next(50, [Math]::Max(51, $Width - 50))
        $y = $random.Next(50, [Math]::Max(51, $Height - 50))
        $Graphics.FillEllipse($brushes[$i % $brushes.Count], $x, $y, $size, [Math]::Max(2, [int]($size * (0.55 + $random.NextDouble()))))
    }
    foreach ($brush in $brushes) { $brush.Dispose() }
}

function Add-Coin {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$X,
        [int]$Y,
        [int]$Size
    )

    $random = New-Object System.Random ($X * 17 + $Y * 31 + $Size)
    $cx = $X + $Size / 2.0
    $cy = $Y + $Size * 0.47
    $rx = $Size / 2.0
    $ry = $Size * 0.44
    $points = New-Object 'System.Drawing.Point[]' 20
    $shadowPoints = New-Object 'System.Drawing.Point[]' 20
    for ($i = 0; $i -lt 20; $i++) {
        $angle = [Math]::PI * 2 * $i / 20.0
        $jitter = 0.86 + $random.NextDouble() * 0.23
        $px = [int]($cx + [Math]::Cos($angle) * $rx * $jitter)
        $py = [int]($cy + [Math]::Sin($angle) * $ry * $jitter)
        $points[$i] = [System.Drawing.Point]::new($px, $py)
        $shadowPoints[$i] = [System.Drawing.Point]::new($px + 6, $py + 8)
    }

    $shadow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(205, $ink))
    $goldBrush = New-Object System.Drawing.SolidBrush $darkGold
    $outline = New-Pen $ink ([Math]::Max(6, [int]($Size / 13)))
    $ring = New-Pen ([System.Drawing.Color]::FromArgb(205, $bright)) ([Math]::Max(3, [int]($Size / 18)))
    $dim = New-Pen ([System.Drawing.Color]::FromArgb(160, 110, 70, 18)) ([Math]::Max(3, [int]($Size / 24)))

    $Graphics.FillClosedCurve($shadow, $shadowPoints, [System.Drawing.Drawing2D.FillMode]::Winding, 0.38)
    $Graphics.FillClosedCurve($goldBrush, $points, [System.Drawing.Drawing2D.FillMode]::Winding, 0.38)
    $Graphics.DrawClosedCurve($outline, $points, 0.38, [System.Drawing.Drawing2D.FillMode]::Winding)
    $Graphics.DrawCurve($ring, @(
        [System.Drawing.Point]::new([int]($X + $Size * 0.18), [int]($Y + $Size * 0.28)),
        [System.Drawing.Point]::new([int]($X + $Size * 0.36), [int]($Y + $Size * 0.16)),
        [System.Drawing.Point]::new([int]($X + $Size * 0.58), [int]($Y + $Size * 0.19))
    ), 0.45)
    $Graphics.DrawCurve($dim, @(
        [System.Drawing.Point]::new([int]($X + $Size * 0.26), [int]($Y + $Size * 0.75)),
        [System.Drawing.Point]::new([int]($X + $Size * 0.55), [int]($Y + $Size * 0.84)),
        [System.Drawing.Point]::new([int]($X + $Size * 0.82), [int]($Y + $Size * 0.62))
    ), 0.45)
    $shadow.Dispose()
    $goldBrush.Dispose()
    $outline.Dispose()
    $ring.Dispose()
    $dim.Dispose()
}

function Add-Contract {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height
    )

    $paper = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(238, 218, 194, 110))
    $edge = New-Pen $ink 7
    $line = New-Pen ([System.Drawing.Color]::FromArgb(210, 65, 44, 22)) 3
    $points = @(
        [System.Drawing.Point]::new($X + 10, $Y),
        [System.Drawing.Point]::new($X + [int]($Width * 0.36), $Y + 9),
        [System.Drawing.Point]::new($X + [int]($Width * 0.72), $Y + 1),
        [System.Drawing.Point]::new($X + $Width - 5, $Y + 18),
        [System.Drawing.Point]::new($X + $Width - 12, $Y + [int]($Height * 0.58)),
        [System.Drawing.Point]::new($X + $Width - 22, $Y + $Height - 7),
        [System.Drawing.Point]::new($X + [int]($Width * 0.54), $Y + $Height - 1),
        [System.Drawing.Point]::new($X + 4, $Y + $Height - 14)
    )
    $Graphics.FillPolygon($paper, $points)
    $Graphics.DrawPolygon($edge, $points)
    for ($i = 0; $i -lt 4; $i++) {
        $yy = $Y + 18 + $i * 18
        $Graphics.DrawCurve($line, @(
            [System.Drawing.Point]::new($X + 20, $yy),
            [System.Drawing.Point]::new($X + [int]($Width * 0.46), $yy + (($i % 2) * 5)),
            [System.Drawing.Point]::new($X + $Width - 28, $yy + 2)
        ), 0.35)
    }
    $paper.Dispose()
    $edge.Dispose()
    $line.Dispose()
}

function New-IconAsset {
    param(
        [string]$Path,
        [string]$Kind,
        [int]$Seed = 1,
        [int]$Size = 512
    )

    $canvas = New-Canvas $Size $Size
    $bitmap = $canvas[0]
    $graphics = $canvas[1]
    Add-BrushStrokes $graphics $Size $Size $Seed

    $outline = New-Pen $ink 12
    $gold = New-Pen $darkGold 8
    $light = New-Pen $bright 4
    $accentPen = New-Pen $accent 4
    $goldBrush = New-Object System.Drawing.SolidBrush $darkGold
    $lightBrush = New-Object System.Drawing.SolidBrush $bright
    $inkBrush = New-Object System.Drawing.SolidBrush $ink

    switch ($Kind) {
        "amulet" {
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(250, 96),
                [System.Drawing.Point]::new(160, 150),
                [System.Drawing.Point]::new(150, 286),
                [System.Drawing.Point]::new(248, 386),
                [System.Drawing.Point]::new(356, 300),
                [System.Drawing.Point]::new(346, 150),
                [System.Drawing.Point]::new(250, 96)
            ) $darkGold 34
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(255, 165),
                [System.Drawing.Point]::new(238, 215),
                [System.Drawing.Point]::new(258, 306)
            ) $accent 12
            Add-Splatter $graphics $Size $Size ($Seed + 100) 18
        }
        "wager" {
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(124, 302),
                [System.Drawing.Point]::new(190, 176),
                [System.Drawing.Point]::new(330, 146),
                [System.Drawing.Point]::new(386, 262)
            ) $accent 10
            Add-Coin $graphics 146 152 138
            Add-Coin $graphics 244 218 118
            Add-Splatter $graphics $Size $Size ($Seed + 100) 16
        }
        "throw" {
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(82, 360),
                [System.Drawing.Point]::new(158, 308),
                [System.Drawing.Point]::new(235, 238),
                [System.Drawing.Point]::new(330, 160),
                [System.Drawing.Point]::new(430, 126)
            ) $darkGold 18
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(105, 372),
                [System.Drawing.Point]::new(206, 310),
                [System.Drawing.Point]::new(322, 222),
                [System.Drawing.Point]::new(412, 162)
            ) $bright 8
            for ($i = 0; $i -lt 5; $i++) { Add-Coin $graphics (112 + $i * 56) (318 - $i * 45) 54 }
            Add-Splatter $graphics $Size $Size ($Seed + 100) 16
        }
        "rain" {
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(105, 310),
                [System.Drawing.Point]::new(175, 395),
                [System.Drawing.Point]::new(285, 392),
                [System.Drawing.Point]::new(392, 294)
            ) $accent 12
            $coinPoints = @(@(82,96),@(178,88),@(292,103),@(118,214),@(246,222),@(368,198),@(183,334),@(320,330))
            foreach ($p in $coinPoints) { Add-Coin $graphics $p[0] $p[1] 58 }
            Add-Splatter $graphics $Size $Size ($Seed + 100) 20
        }
        "check" {
            Add-Contract $graphics 108 126 294 230
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(150, 162),
                [System.Drawing.Point]::new(222, 220),
                [System.Drawing.Point]::new(350, 318)
            ) $darkGold 10
            Add-Splatter $graphics $Size $Size ($Seed + 100) 12
        }
        "age" {
            Add-Coin $graphics 128 138 250
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(255, 98),
                [System.Drawing.Point]::new(275, 180),
                [System.Drawing.Point]::new(260, 258),
                [System.Drawing.Point]::new(248, 392)
            ) $bright 16
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(120, 240),
                [System.Drawing.Point]::new(205, 220),
                [System.Drawing.Point]::new(300, 235),
                [System.Drawing.Point]::new(405, 218)
            ) $bright 15
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(158, 350),
                [System.Drawing.Point]::new(222, 282),
                [System.Drawing.Point]::new(290, 210),
                [System.Drawing.Point]::new(356, 142)
            ) $darkGold 18
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(150, 142),
                [System.Drawing.Point]::new(214, 205),
                [System.Drawing.Point]::new(290, 274),
                [System.Drawing.Point]::new(370, 352)
            ) $darkGold 18
            Add-Splatter $graphics $Size $Size ($Seed + 100) 14
        }
        "false_gold" {
            Add-Coin $graphics 120 130 260
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(165, 196),
                [System.Drawing.Point]::new(235, 248),
                [System.Drawing.Point]::new(338, 314)
            ) $accent 11
        }
        "debt" {
            Add-Contract $graphics 120 116 270 270
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(152, 162),
                [System.Drawing.Point]::new(245, 242),
                [System.Drawing.Point]::new(360, 338)
            ) $darkGold 10
            Add-BrushCurve $graphics @(
                [System.Drawing.Point]::new(360, 160),
                [System.Drawing.Point]::new(255, 240),
                [System.Drawing.Point]::new(152, 340)
            ) $darkGold 10
        }
        "raven" {
            $graphics.FillEllipse($inkBrush, 130, 142, 215, 190)
            $graphics.FillPolygon($inkBrush, @(
                [System.Drawing.Point]::new(316, 205),
                [System.Drawing.Point]::new(430, 230),
                [System.Drawing.Point]::new(320, 258)
            ))
            $graphics.FillEllipse($lightBrush, 278, 185, 24, 24)
            Add-Contract $graphics 226 260 140 110
        }
        "contract" {
            Add-Contract $graphics 116 88 280 315
            Add-Coin $graphics 210 286 100
        }
        "audit" {
            Add-Coin $graphics 120 116 260
            $graphics.DrawArc($outline, 110, 110, 290, 290, 35, 285)
            $graphics.DrawArc($accentPen, 110, 110, 290, 290, 35, 285)
            $graphics.DrawLine($outline, 256, 130, 256, 376)
            $graphics.DrawLine($light, 256, 130, 256, 376)
        }
        default {
            Add-Coin $graphics 126 126 260
        }
    }

    $outline.Dispose()
    $gold.Dispose()
    $light.Dispose()
    $accentPen.Dispose()
    $goldBrush.Dispose()
    $lightBrush.Dispose()
    $inkBrush.Dispose()
    $graphics.Dispose()
    Save-Bitmap $bitmap $Path
    $bitmap.Dispose()
}

function Copy-ReferenceCrop {
    param(
        [string]$Source,
        [string]$Path,
        [int]$Size
    )

    $src = [System.Drawing.Image]::FromFile($Source)
    try {
        $bitmap = New-Object System.Drawing.Bitmap $Size, $Size
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $srcSize = [Math]::Min($src.Width, $src.Height)
        $srcRect = New-Object System.Drawing.Rectangle ([int](($src.Width - $srcSize) / 2)), ([int](($src.Height - $srcSize) / 2)), $srcSize, $srcSize
        $dstRect = New-Object System.Drawing.Rectangle 0, 0, $Size, $Size
        $graphics.DrawImage($src, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
        $graphics.Dispose()
        Save-Bitmap $bitmap $Path
        $bitmap.Dispose()
    }
    finally {
        $src.Dispose()
    }
}

if (Test-Path -LiteralPath $ReferenceImage) {
    try {
        Copy-Item -LiteralPath $ReferenceImage -Destination (Join-Path $modRoot "ModResource\Images\CardPack\_gold_dream_style_reference.png") -Force
        Copy-ReferenceCrop -Source $ReferenceImage -Path (Join-Path $modRoot "ModResource\Images\CardPack\cardpack_gold_dream.png") -Size 768
        Copy-ReferenceCrop -Source $ReferenceImage -Path (Join-Path $modRoot "Icon.png") -Size 512
        Copy-ReferenceCrop -Source $ReferenceImage -Path (Join-Path $modRoot "ModResource\Images\Character\GoldWitch.png") -Size 768
        Copy-ReferenceCrop -Source $ReferenceImage -Path (Join-Path $modRoot "ModResource\Images\CareerImage\GoldWitch.png") -Size 768
        Copy-ReferenceCrop -Source $ReferenceImage -Path (Join-Path $modRoot "ModResource\Images\Dialogue\GoldWitch.png") -Size 512
        Copy-ReferenceCrop -Source $ReferenceImage -Path (Join-Path $modRoot "ModResource\Images\Avatar\GoldWitch.png") -Size 256
        Copy-ReferenceCrop -Source $ReferenceImage -Path (Join-Path $modRoot "ModResource\Images\Icon\GoldWitch.png") -Size 256
        Copy-ReferenceCrop -Source $ReferenceImage -Path (Join-Path $modRoot "ModResource\Images\Role\GoldWitch\roledata_avatar.png") -Size 256
    }
    catch {
        Write-Warning "Skipped reference image refresh because the file is currently locked: $($_.Exception.Message)"
    }
}

if ($RegenerateProceduralCardIcons) {
    New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Card\GoldExp\gilded_amulet.png") -Kind "amulet" -Seed 11
    New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Card\GoldExp\gold_dream_wager.png") -Kind "wager" -Seed 12
    New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Card\GoldExp\fortune_throw.png") -Kind "throw" -Seed 13
    New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Card\GoldExp\false_gold_rain.png") -Kind "rain" -Seed 14
    New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Card\GoldExp\blank_check.png") -Kind "check" -Seed 15
    New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Card\GoldExp\golden_age.png") -Kind "age" -Seed 16
}
New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Buff\GoldExp\false_gold.png") -Kind "false_gold" -Seed 21 -Size 256
New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Buff\GoldExp\debt.png") -Kind "debt" -Seed 22 -Size 256
New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Buff\GoldExp\midas_raven_trait.png") -Kind "raven" -Seed 23 -Size 256
New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Relic\GoldExp\old_king_coin.png") -Kind "age" -Seed 31 -Size 512
New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Relic\GoldExp\bankruptcy_contract.png") -Kind "contract" -Seed 32 -Size 512
New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Skill\midas_contract.png") -Kind "contract" -Seed 41 -Size 512
New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Skill\final_audit.png") -Kind "audit" -Seed 42 -Size 512
New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Partner\GoldExp\midas_raven.png") -Kind "raven" -Seed 51 -Size 512
New-IconAsset -Path (Join-Path $modRoot "ModResource\Images\Partner\GoldExp\midas_raven_choice.png") -Kind "raven" -Seed 52 -Size 512

foreach ($anim in @("Idle", "Attack", "Skill", "Hit", "Defend")) {
    $dir = Join-Path $modRoot "ModResource\AnimationLib\GoldWitch\$anim"
    New-IconAsset -Path (Join-Path $dir "${anim}_00.png") -Kind "audit" -Seed (60 + $anim.Length) -Size 256
    [System.IO.File]::WriteAllText((Join-Path $dir "config.json"), "{`"FrameCount`":1,`"FrameRate`":8}", [System.Text.UTF8Encoding]::new($false))
}

$ravenIdle = Join-Path $modRoot "ModResource\AnimationLib\MidasRaven\Idle"
New-IconAsset -Path (Join-Path $ravenIdle "Idle_00.png") -Kind "raven" -Seed 71 -Size 256
[System.IO.File]::WriteAllText((Join-Path $ravenIdle "config.json"), "{`"FrameCount`":1,`"FrameRate`":8}", [System.Text.UTF8Encoding]::new($false))

Write-Host "GoldExp assets generated."
