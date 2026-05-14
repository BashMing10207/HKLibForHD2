$p1='C:\Users\HP\Desktop\UTIL\HAVOK\HKLib-main\HKLib-main\HKLib.CLI\bin\Release\net7.0\avatar_helldiver.ik_skeleton.hkx.bak'
$p2='C:\Users\HP\Desktop\UTIL\HAVOK\HKLib-main\HKLib-main\HKLib.CLI\bin\Release\net7.0\avatar_helldiver.ik_skeleton.hkx'
$h1=Get-FileHash -Path $p1 -Algorithm SHA256
$h2=Get-FileHash -Path $p2 -Algorithm SHA256
Write-Output "HASH.BAK: $($h1.Hash)"
Write-Output "HASH.NEW: $($h2.Hash)"
$b1=[System.IO.File]::ReadAllBytes($p1)
$b2=[System.IO.File]::ReadAllBytes($p2)
$len=[math]::Min($b1.Length,$b2.Length)
$segs=@()
$in=$false
$start=0
for($i=0;$i -lt $len;$i++){
    if($b1[$i] -ne $b2[$i]){
        if(-not $in){ $in=$true; $start=$i }
    } elseif($in){
        $segs += @{start=$start; len=($i-$start)}
        $in=$false
    }
}
if($in){ $segs += @{start=$start; len=($len-$start)} }
if($b1.Length -ne $b2.Length){ $segs += @{start=$len; len=[math]::Abs($b1.Length-$b2.Length); note='tail'} }
Write-Output "Lengths: bak=$($b1.Length) new=$($b2.Length)"
Write-Output "Differing segments: $($segs.Count)"
foreach($s in $segs){
    $sEnd = $s.start + $s.len - 1
    Write-Output "seg: $($s.start)-$($sEnd) len=$($s.len) $($s.note)"
    $hexLen = [math]::Min(32, $s.len)
    $end1 = [math]::Min($s.start + $hexLen -1, $b1.Length-1)
    $end2 = [math]::Min($s.start + $hexLen -1, $b2.Length-1)
    $hex1 = -join ($b1[$s.start..$end1] | ForEach-Object { $_.ToString('x2') })
    $hex2 = -join ($b2[$s.start..$end2] | ForEach-Object { $_.ToString('x2') })
    Write-Output "  bak: $hex1"
    Write-Output "  new: $hex2"
}