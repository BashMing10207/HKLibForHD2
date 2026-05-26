$bytes = [System.IO.File]::ReadAllBytes('global.havok_physics_properties.main')
$start = -1
for($i=0; $i -lt $bytes.Length - 4; $i++) {
    if($bytes[$i] -eq 84 -and $bytes[$i+1] -eq 65 -and $bytes[$i+2] -eq 71 -and $bytes[$i+3] -eq 48) {
        $start = $i
        break
    }
}
if ($start -ge 0) {
    Write-Output "Found TAG0 at $start"
    $numSec = [BitConverter]::ToUInt32($bytes, $start + 24)
    Write-Output "NumSec: $numSec"
    for($s=0; $s -lt $numSec; $s++) {
        $secStart = $start + 40 + ($s * 32)
        $name = [System.Text.Encoding]::ASCII.GetString($bytes, $secStart, 16)
        $name = $name -replace "`0", "\0"
        Write-Output "Section $s : $name"
    }
} else {
    Write-Output "TAG0 not found."
}