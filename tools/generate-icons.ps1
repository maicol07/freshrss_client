# Regenerates the FreshRSS Client icon set from a single vector-ish definition.
# Run from the repository root:  pwsh -File tools/generate-icons.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# --- Brand palette (single source of truth: recolor here) ---------------------
$PlateTop    = [System.Drawing.Color]::FromArgb(255, 245, 144, 31)   # #F5901F
$PlateBottom = [System.Drawing.Color]::FromArgb(255, 232, 99, 10)    # #E8630A
$Glyph       = [System.Drawing.Color]::White

$AssetsDir = Join-Path $PSScriptRoot '..\FreshRssClient\Assets'
$AssetsDir = [System.IO.Path]::GetFullPath($AssetsDir)

# --- Drawing primitives ------------------------------------------------------

function New-RoundedPath {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Draws the RSS mark (dot + two arcs) inside a square box of side $Side whose
# top-left corner sits at ($X, $Y). All metrics are ratios of $Side so the mark
# scales identically at every resolution.
function Add-RssMark {
    param(
        [System.Drawing.Graphics]$G,
        [single]$X, [single]$Y, [single]$Side,
        [System.Drawing.Color]$Color
    )

    # Origin of the concentric system: bottom-left of the box, inset a little.
    $ox = $X + $Side * 0.215
    $oy = $Y + $Side * 0.785

    $dotR = $Side * 0.105
    $stroke = $Side * 0.135
    $r1 = $Side * 0.34
    $r2 = $Side * 0.60

    $brush = New-Object System.Drawing.SolidBrush($Color)
    $G.FillEllipse($brush, $ox - $dotR, $oy - $dotR, $dotR * 2, $dotR * 2)
    $brush.Dispose()

    $pen = New-Object System.Drawing.Pen($Color, $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($r in @($r1, $r2)) {
        $G.DrawArc($pen, $ox - $r, $oy - $r, $r * 2, $r * 2, 270, 90)
    }
    $pen.Dispose()
}

# -Unplated renders the bare mark on transparency (altform-unplated / lock screen
# assets, which Windows composites without a backing plate). $PlateRatio is the
# plate side as a fraction of the canvas' short edge.
function New-IconBitmap {
    param(
        [int]$Width,
        [int]$Height,
        [switch]$Unplated,
        [System.Drawing.Color]$UnplatedColor = $PlateBottom,
        [single]$PlateRatio = 0.86,
        [single]$MarkRatio = 0.62
    )

    $bmp = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $short = [Math]::Min($Width, $Height)

    if ($Unplated) {
        # No backing plate: Windows composes these over the taskbar / title bar /
        # lock screen, so the mark itself carries the color and fills more canvas.
        $side = $short * 0.86
        Add-RssMark -G $g -X (($Width - $side) / 2) -Y (($Height - $side) / 2) -Side $side -Color $UnplatedColor
    }
    else {
        $plate = $short * $PlateRatio
        $px = ($Width - $plate) / 2
        $py = ($Height - $plate) / 2
        $radius = $plate * 0.22

        $path = New-RoundedPath -X $px -Y $py -W $plate -H $plate -R $radius
        $rect = New-Object System.Drawing.RectangleF($px, $py, $plate, $plate)
        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $PlateTop, $PlateBottom, 90.0)
        $g.FillPath($grad, $path)
        $grad.Dispose()
        $path.Dispose()

        $side = $plate * $MarkRatio
        Add-RssMark -G $g -X ($px + ($plate - $side) / 2) -Y ($py + ($plate - $side) / 2) -Side $side -Color $Glyph
    }

    $g.Dispose()
    return $bmp
}

function Save-Png {
    param([System.Drawing.Bitmap]$Bitmap, [string]$Name)

    $path = Join-Path $AssetsDir $Name
    $Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  $Name  ($($Bitmap.Width)x$($Bitmap.Height))"
}

# System.Drawing's Icon.Save() only serializes a single frame, so the ICO
# container is written by hand. PNG-compressed frames are valid for every size
# on Windows 10 1809+ (the project targets 10.0.19041).
function Save-MultiSizeIco {
    param([string]$Name, [int[]]$Sizes)

    $frames = foreach ($s in $Sizes) {
        $bmp = New-IconBitmap -Width $s -Height $s
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
    }

    $path = Join-Path $AssetsDir $Name
    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter($fs)

    $w.Write([uint16]0)                  # reserved
    $w.Write([uint16]1)                  # type: icon
    $w.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($f in $frames) {
        $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $w.Write([byte]$dim)             # width  (0 == 256)
        $w.Write([byte]$dim)             # height
        $w.Write([byte]0)                # palette entries
        $w.Write([byte]0)                # reserved
        $w.Write([uint16]1)              # color planes
        $w.Write([uint16]32)             # bits per pixel
        $w.Write([uint32]$f.Bytes.Length)
        $w.Write([uint32]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $w.Write($f.Bytes) }

    $w.Dispose()
    $fs.Dispose()
    Write-Host "  $Name  ($($Sizes -join ', '))"
}

# --- Asset set ---------------------------------------------------------------
# Names must match the existing files exactly: the manifest resolves them
# through MRT scale qualifiers, so renaming would break packaging.

Write-Host "Writing assets to $AssetsDir"

$targets = @(
    @{ Name = 'Square44x44Logo.scale-200.png';   W = 88;   H = 88 }
    @{ Name = 'Square150x150Logo.scale-200.png'; W = 300;  H = 300 }
    @{ Name = 'StoreLogo.png';                   W = 50;   H = 50 }
    # Non-square canvases: the plate is deliberately smaller than the tile ratio,
    # otherwise the splash reads as one giant orange square.
    @{ Name = 'SplashScreen.scale-200.png';      W = 1240; H = 600; PlateRatio = 0.42 }
    @{ Name = 'Wide310x150Logo.scale-200.png';   W = 620;  H = 300; PlateRatio = 0.62 }
)

foreach ($t in $targets) {
    $ratio = if ($t.ContainsKey('PlateRatio')) { $t.PlateRatio } else { 0.86 }
    $bmp = New-IconBitmap -Width $t.W -Height $t.H -PlateRatio $ratio
    Save-Png -Bitmap $bmp -Name $t.Name
    $bmp.Dispose()
}

# Unplated variants: Windows draws no backing plate, so the mark carries the color.
# The lock screen badge must be white pixels plus alpha only. The two taskbar /
# title bar altforms are theme-specific: brighter orange on dark, deeper on light.
foreach ($t in @(
        @{ Name = 'LockScreenLogo.scale-200.png';                            S = 48; C = $Glyph }
        @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png';       S = 24; C = $PlateTop }
        @{ Name = 'Square44x44Logo.targetsize-48_altform-lightunplated.png';  S = 48; C = $PlateBottom }
    )) {
    $bmp = New-IconBitmap -Width $t.S -Height $t.S -Unplated -UnplatedColor $t.C
    Save-Png -Bitmap $bmp -Name $t.Name
    $bmp.Dispose()
}

Save-MultiSizeIco -Name 'AppIcon.ico' -Sizes @(16, 20, 24, 32, 48, 64, 128, 256)

Write-Host 'Done.'
