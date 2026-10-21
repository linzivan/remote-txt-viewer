param(
  [string]$Dir = "C:\Users\18321\AppData\Local\Temp\rtv_multi"
)

Add-Type -AssemblyName System.Security

function Enc([string]$plain) {
  $b = [System.Text.Encoding]::UTF8.GetBytes($plain)
  $e = [System.Security.Cryptography.ProtectedData]::Protect(
        $b, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
  return [Convert]::ToBase64String($e)
}

New-Item -ItemType Directory -Force -Path $Dir | Out-Null
$file = Join-Path $Dir "sessions.ini"

$pwd = Enc "123456"

$lines = @(
  "# multi-session test",
  "",
  "[global]",
  "active=0",
  "font_size=10",
  "follow=1",
  "word_wrap=0",
  "show_timeline=1",
  "win_x=-1",
  "win_y=-1",
  "win_w=0",
  "win_h=0",
  "win_max=0",
  "",
  "[session.0]",
  "host=127.0.0.1",
  "port=2222",
  "user=test",
  "password=$pwd",
  "use_key=0",
  "key_file=",
  "key_pass=",
  "remote_path=/var/log/app.log",
  "interval=2",
  "encoding=auto",
  "host_key_fp=",
  "",
  "[session.1]",
  "host=127.0.0.1",
  "port=2222",
  "user=test",
  "password=$pwd",
  "use_key=0",
  "key_file=",
  "key_pass=",
  "remote_path=/var/log/notes.txt",
  "interval=3",
  "encoding=auto",
  "host_key_fp="
)

[System.IO.File]::WriteAllLines($file, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "sessions.ini -> $file"
