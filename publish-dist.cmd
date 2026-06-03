@echo off
setlocal
pushd "%~dp0"

dotnet publish src\FirewiseMail.App\FirewiseMail.App.csproj -c Release -o dist\FirewiseMail
if errorlevel 1 goto :fail

> dist\FirewiseMail\FirewiseMail-1366.cmd echo @echo off
>> dist\FirewiseMail\FirewiseMail-1366.cmd echo start "" "%%~dp0FirewiseMail.App.exe" --layout 1366
if errorlevel 1 goto :fail

> dist\FirewiseMail\FirewiseMail-1080.cmd echo @echo off
>> dist\FirewiseMail\FirewiseMail-1080.cmd echo start "" "%%~dp0FirewiseMail.App.exe" --layout 1080
if errorlevel 1 goto :fail

xcopy /E /I /Y docs dist\FirewiseMail\docs >nul
if errorlevel 1 goto :fail

popd
exit /b 0

:fail
popd
exit /b 1
