@echo off
setlocal EnableExtensions EnableDelayedExpansion

IF "%1"=="" (
	for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "[xml]$p = Get-Content 'LoqNova.WPF\LoqNova.WPF.csproj'; ($p.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version"`) do set VERSION=%%v
) ELSE (
	SET VERSION=%1
)

if "%VERSION%"=="" (
	echo ERROR: Could not resolve application version from LoqNova.WPF\LoqNova.WPF.csproj
	exit /b 1
)

SET PATH=%PATH%;"C:\Program Files (x86)\Inno Setup 6"

dotnet publish LoqNova.WPF -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LoqNova.SpectrumTester -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LoqNova.CLI -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b

iscc make_installer.iss /DMyAppVersion=%VERSION% /DMyOutputBaseFilename=LoqNova-v%VERSION%-setup || exit /b
