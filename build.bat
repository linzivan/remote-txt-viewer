@echo off
rem Build RemoteTxtViewer.exe with the compiler shipped in Windows (.NET Framework 4.x)
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found ^(.NET Framework 4.x compiler^)
  exit /b 1
)

set SSHNET=lib\Renci.SshNet.dll
if not exist "%SSHNET%" (
  echo [ERROR] missing %SSHNET%
  exit /b 1
)

echo Compiling ...
"%CSC%" /nologo /target:winexe /platform:anycpu /codepage:65001 /optimize+ ^
  /out:RemoteTxtViewer.exe ^
  /win32icon:app.ico ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:System.Security.dll ^
  /r:%SSHNET% ^
  /resource:%SSHNET%,Renci.SshNet.dll ^
  src\AppConfig.cs src\SftpFetcher.cs src\ChangeLog.cs src\FavoritesStore.cs ^
  src\PromptForm.cs src\Program.cs src\MainForm.cs src\SettingsForm.cs ^
  src\RemotePickerForm.cs src\FavoritesForm.cs

if errorlevel 1 (
  echo [FAILED]
  exit /b 1
)

echo [OK] built RemoteTxtViewer.exe
for %%F in (RemoteTxtViewer.exe) do echo    size: %%~zF bytes
