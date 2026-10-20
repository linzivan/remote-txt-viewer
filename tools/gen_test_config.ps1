param([string]$Dir)

if (-not $Dir) { $Dir = Join-Path $env:TEMP "rtv_ui_test" }

Add-Type -AssemblyName System.Security

$bytes = [System.Text.Encoding]::UTF8.GetBytes("123456")
$enc = [System.Security.Cryptography.ProtectedData]::Protect(
    $bytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
$b64 = [Convert]::ToBase64String($enc)

New-Item -ItemType Directory -Force -Path $Dir | Out-Null
$file = Join-Path $Dir "config.ini"

$lines = @(
    "# test profile",
    "host=127.0.0.1",
    "port=2222",
    "user=test",
    "password=$b64",
    "use_key=0",
    "key_file=",
    "key_pass=",
    "remote_path=/var/log/app.log",
    "interval=1",
    "encoding=auto",
    "follow=1",
    "word_wrap=0",
    "font_size=10",
    "host_key_fp=",
    "show_timeline=1",
    "win_x=-1",
    "win_y=-1",
    "win_w=0",
    "win_h=0",
    "win_max=0"
)
[System.IO.File]::WriteAllLines($file, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "test config -> $file"
