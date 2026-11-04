@echo off
setlocal EnableExtensions
set ROOT=%~dp0
set OUT=%ROOT%Skylarping-XP
set BIN=%ROOT%Skylarping\bin\Release

:: Find MSBuild
for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set MSBUILD=%%i
if not defined MSBUILD (echo ERROR: MSBuild not found. & exit /b 1)

:: Build solution
echo Building solution ...
"%MSBUILD%" "%ROOT%Skylarping.sln" /restore /t:Rebuild /p:Configuration=Release /m /v:minimal || exit /b %ERRORLEVEL%
echo.

:: Stage output (everything MSBuild put in bin\Release, minus debug symbols and XML docs)
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"
robocopy "%BIN%" "%OUT%" /E /XF *.pdb *.xml *.vshost.* /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (echo ERROR: Copying build output failed. & exit /b 1)
copy /Y "%ROOT%LICENSE.md" "%OUT%\" >nul

:: Optional deps from deps\ (curl is the HTTPS fallback on XP; it is looked up next to the EXE)
echo Packaging optional dependencies ...
call :CopyIfExists "%ROOT%deps\curl.exe" "%OUT%\curl.exe"
call :CopyIfExists "%ROOT%deps\curl-ca-bundle.crt" "%OUT%\curl-ca-bundle.crt"
if not exist "%OUT%\curl.exe" echo   WARNING: deps\curl.exe not found - HTTPS will not work on XP.
:: Optional SimpleLLMChat-compatible tools: deps\tools\<package>\ (exe + json + its DLLs). The
:: project copies them into the build output as tools\<package>\; none are required.
if exist "%ROOT%deps\tools" (
  for /d %%D in ("%ROOT%deps\tools\*") do echo   packaged tools\%%~nxD
) else (
  echo   no deps\tools folder - building without tools
)

:: Optional third-party license texts (for the deps above; the build already ships its own)
call :CopyLicenses

echo.
echo Done: %OUT%
exit /b 0

:CopyIfExists
if exist "%~1" (
  copy /Y "%~1" "%~2" >nul
  echo   packaged %~nx2
)
exit /b 0

:CopyLicenses
if not exist "%ROOT%THIRD_PARTY_LICENSES" exit /b 0
set "_any="
for %%F in ("%ROOT%THIRD_PARTY_LICENSES\*") do (
  if /I not "%%~nxF"==".gitkeep" set "_any=1"
)
if not defined _any exit /b 0
mkdir "%OUT%\THIRD_PARTY_LICENSES" 2>nul
for %%F in ("%ROOT%THIRD_PARTY_LICENSES\*") do (
  if /I not "%%~nxF"==".gitkeep" (
    copy /Y "%%F" "%OUT%\THIRD_PARTY_LICENSES\" >nul
    echo   packaged THIRD_PARTY_LICENSES\%%~nxF
  )
)
exit /b 0
