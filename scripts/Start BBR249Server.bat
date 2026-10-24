@echo off
cd /d "%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -WindowStyle Hidden -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WindowStyle Hidden"
    exit /b
)

if exist BBR249Server.exe.new move /y BBR249Server.exe.new BBR249Server.exe >nul

start "" BBR249Server.exe
