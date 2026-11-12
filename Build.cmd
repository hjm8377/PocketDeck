@echo off
setlocal
cd /d "%~dp0"
set "DECK_COMPILER=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%DECK_COMPILER%" set "DECK_COMPILER=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist dist mkdir dist
set "DECK_REPOSITORY=%POCKETDECK_REPOSITORY%"
if not defined DECK_REPOSITORY set "DECK_REPOSITORY=%GITHUB_REPOSITORY%"
>dist\repository.txt <nul set /p "=%DECK_REPOSITORY%"
set "DECK_RESOURCES=/resource:phone\index.html,Phone.index.html /resource:phone\phone.css,Phone.phone.css /resource:phone\phone.js,Phone.phone.js"
"%DECK_COMPILER%" /nologo /target:winexe /main:Program /codepage:65001 /optimize+ /win32manifest:app.manifest /out:dist\PocketDeck.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll %DECK_RESOURCES% /resource:dist\repository.txt,PocketDeck.Repository PocketDeck.cs AudioMixer.cs InstallPaths.cs UpdateClient.cs BuildInfo.cs
if errorlevel 1 exit /b 1
"%DECK_COMPILER%" /nologo /target:winexe /main:Setup /codepage:65001 /optimize+ /win32manifest:app.manifest /out:dist\PocketDeckSetup.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll /resource:dist\PocketDeck.exe,PocketDeck.Payload Setup.cs InstallPaths.cs BuildInfo.cs
if errorlevel 1 exit /b 1
echo Built dist\PocketDeckSetup.exe
