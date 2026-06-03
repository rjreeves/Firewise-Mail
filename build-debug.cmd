@echo off
setlocal
pushd "%~dp0"

dotnet build FirewiseMail.slnx -c Debug

popd
exit /b %ERRORLEVEL%
