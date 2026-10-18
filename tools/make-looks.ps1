# Generates the built-in grades as 33-point 3D LUTs (.cube) into src/Aether.App/Assets/Looks.
# Run it again after changing a recipe below; the .cube files are checked in so a build needs no script.
param([string]$OutDir = (Join-Path $PSScriptRoot '..\src\Aether.App\Assets\Looks'))

$size = 33
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Clamp([double]$v) { [Math]::Min(1.0, [Math]::Max(0.0, $v)) }

# Each recipe takes red, green, blue in 0..1 and returns the graded triple.
$recipes = [ordered]@{
    'warm' = { param($r, $g, $b)
        @((Clamp ($r * 1.06 + 0.01)), $g, (Clamp ($b * 0.90))) }
    'cool' = { param($r, $g, $b)
        @((Clamp ($r * 0.92)), $g, (Clamp ($b * 1.06 + 0.01))) }
    'vivid' = { param($r, $g, $b)
        $y = 0.2126 * $r + 0.7152 * $g + 0.0722 * $b
        $out = foreach ($c in $r, $g, $b) {
            $s = $y + ($c - $y) * 1.35
            # A gentle S-curve for contrast.
            $s = Clamp $s
            Clamp ($s + 0.15 * ($s - 0.5) * (1 - [Math]::Abs(2 * $s - 1)))
        }
        $out }
    'teal-orange' = { param($r, $g, $b)
        $y = 0.2126 * $r + 0.7152 * $g + 0.0722 * $b
        $shadow = [Math]::Pow(1 - $y, 2); $light = [Math]::Pow($y, 2)
        $r2 = $r + 0.10 * $light - 0.07 * $shadow
        $g2 = $g + 0.02 * $shadow - 0.01 * $light
        $b2 = $b + 0.08 * $shadow - 0.08 * $light
        $y2 = 0.2126 * $r2 + 0.7152 * $g2 + 0.0722 * $b2
        @((Clamp ($y2 + ($r2 - $y2) * 1.1)), (Clamp ($y2 + ($g2 - $y2) * 1.1)), (Clamp ($y2 + ($b2 - $y2) * 1.1))) }
    'faded-film' = { param($r, $g, $b)
        $y = 0.2126 * $r + 0.7152 * $g + 0.0722 * $b
        $out = foreach ($c in ($r + 0.02), $g, ($b - 0.02)) {
            $d = $y + ($c - $y) * 0.78
            Clamp (0.06 + $d * 0.89)
        }
        $out }
    'black-white' = { param($r, $g, $b)
        $y = Clamp (0.2126 * $r + 0.7152 * $g + 0.0722 * $b)
        @($y, $y, $y) }
}

foreach ($name in $recipes.Keys) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("TITLE `"$name`"")
    $lines.Add("LUT_3D_SIZE $size")
    # .cube order: red changes fastest, blue slowest.
    for ($bi = 0; $bi -lt $size; $bi++) {
        for ($gi = 0; $gi -lt $size; $gi++) {
            for ($ri = 0; $ri -lt $size; $ri++) {
                $o = & $recipes[$name] ($ri / ($size - 1)) ($gi / ($size - 1)) ($bi / ($size - 1))
                $lines.Add(('{0:0.000000} {1:0.000000} {2:0.000000}' -f $o[0], $o[1], $o[2]))
            }
        }
    }
    $path = Join-Path $OutDir "$name.cube"
    [System.IO.File]::WriteAllText($path, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host "wrote $path"
}
