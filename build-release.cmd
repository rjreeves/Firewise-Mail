@echo off
setlocal
pushd "%~dp0"

dotnet build FirewiseMail.slnx -c Release

popd
exit /b %ERRORLEVEL%
