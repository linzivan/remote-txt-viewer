Add-Type -AssemblyName System.Security

$plain = "123456"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($plain)
$enc = [System.Security.Cryptography.ProtectedData]::Protect(
    $bytes, $null,
    [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
$b64 = [Convert]::ToBase64String($enc)

$dir = Join-Path $env:APPDATA "RemoteTxtViewer"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$file = Join-Path $dir "config.ini"

$lines = @(
    "# RemoteTxtViewer 配置文件",
    "host=127.0.0.1",
    "port=2222",
    "user=test",
    "password=$b64",
    "use_key=0",
    "key_file=",
    "key_pass=",
    "remote_path=/var/log/app.log",
    "interval=2",
    "encoding=auto",
    "follow=1",
    "word_wrap=0",
    "font_size=10",
    "win_x=-1",
    "win_y=-1",
    "win_w=0",
    "win_h=0",
    "win_max=0"
)
[System.IO.File]::WriteAllLines($file, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "config written: $file"
