Add-Type -AssemblyName System.Security

$plain = "MySecretPass123"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($plain)

$a = [Convert]::ToBase64String(
    [System.Security.Cryptography.ProtectedData]::Protect($bytes, $null, 'CurrentUser'))
$b = [Convert]::ToBase64String(
    [System.Security.Cryptography.ProtectedData]::Protect($bytes, $null, 'CurrentUser'))

Write-Output "明文            : $plain"
Write-Output "加密第 1 次      : $a"
Write-Output "加密第 2 次      : $b"
Write-Output "两次密文相同?    : $($a -eq $b)"
Write-Output "密文长度         : $($a.Length) 字符"
