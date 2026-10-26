@echo off
rem Import the self-signed cert into CurrentUser trusted root + trusted publisher.
rem Needed so Windows shows RemoteTxtViewer as a known publisher instead of "unknown".
setlocal
cd /d "%~dp0"
set CER=..\RemoteTxtViewer.cer
if not exist "%CER%" (
  echo [ERROR] RemoteTxtViewer.cer not found. Run tools\sign.ps1 first.
  exit /b 1
)
echo Importing %CER% ...
certutil -addstore -user Root "%CER%"
certutil -addstore -user TrustedPublisher "%CER%"
echo.
echo Done. If Windows still warns, unblock the exe:
echo    powershell -Command "Unblock-File 'RemoteTxtViewer.exe'"
