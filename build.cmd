@echo off
setlocal
cd /d "%~dp0"
if not exist dist mkdir dist
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /out:dist\SoundbarKeepAwake.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll /reference:Microsoft.CSharp.dll src\*.cs
if errorlevel 1 exit /b 1
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:SoundbarKeepAwake.Tests /out:dist\Tests.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll /reference:Microsoft.CSharp.dll src\*.cs tests\*.cs
if errorlevel 1 exit /b 1
dist\Tests.exe
if errorlevel 1 exit /b 1
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /out:dist\SoundbarKeepAwake-Setup.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll /reference:Microsoft.CSharp.dll src\*.cs
