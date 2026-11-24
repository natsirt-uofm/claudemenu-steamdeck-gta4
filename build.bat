@echo off
rem Builds scripts\ClaudeMenu.net.dll against your real ScriptHookDotNet.dll.
rem Usage: build.bat "C:\Path\To\GTA IV"   (folder that contains ScriptHookDotNet.dll)
setlocal
set GTADIR=%~1
if "%GTADIR%"=="" set GTADIR=.
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" ( echo .NET Framework 4 compiler not found at %CSC% & exit /b 1 )
if not exist "%GTADIR%\ScriptHookDotNet.dll" ( echo ScriptHookDotNet.dll not found in %GTADIR% & exit /b 1 )
"%CSC%" /nologo /target:library /optimize+ /out:scripts\ClaudeMenu.net.dll /r:"%GTADIR%\ScriptHookDotNet.dll" /r:System.Drawing.dll /r:System.Windows.Forms.dll scripts\ClaudeMenu.cs
if errorlevel 1 exit /b 1
echo Built scripts\ClaudeMenu.net.dll
