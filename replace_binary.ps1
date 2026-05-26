Remove-Item -Recurse -Force HKLib.Serialization/hk2019/Binary
Copy-Item -Recurse HKLib.Serialization/hk2018/Binary HKLib.Serialization/hk2019/Binary

$files = Get-ChildItem -Path HKLib.Serialization/hk2019/Binary -Recurse -Filter *.cs
foreach ($file in $files) {
    $content = Get-Content $file.FullName
    $content = $content -replace 'namespace HKLib.Serialization.hk2018', 'namespace HKLib.Serialization.hk2019'
    $content = $content -replace 'using HKLib.Serialization.hk2018', 'using HKLib.Serialization.hk2019'
    $content = $content -replace '"20180100"', '"20190100"'
    $content = $content -replace 'hkx2018', 'hkx2019'
    Set-Content -Path $file.FullName -Value $content
}
