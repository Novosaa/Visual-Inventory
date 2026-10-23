@echo off
setlocal
cd /d "%~dp0"
title Visual Inventory - set up runtime
echo.
echo  VISUAL INVENTORY - one-time runtime setup
echo  ------------------------------------------
echo  This puts Microsoft's .NET 10.0.12 runtime into the Runtime folder here.
echo  It only reads the two Microsoft zips in this folder. Nothing is downloaded.
echo.

if exist "Runtime\shared\Microsoft.WindowsDesktop.App\10.0.12\PresentationFramework.dll" if exist "Runtime\dotnet.exe" (
  echo  The runtime is already set up. You can open VisualInventory.exe.
  goto :done
)

set "RT="
set "WD="
for %%F in (dotnet-runtime-10.0.12-win-x64*.zip dotnet-runtime-10_0_12-win-x64*.zip) do set "RT=%%F"
for %%F in (windowsdesktop-runtime-10.0.12-win-x64*.zip windowsdesktop-runtime-10_0_12-win-x64*.zip) do set "WD=%%F"

if not defined RT (
  echo  Missing: dotnet-runtime-10.0.12-win-x64.zip
  echo  Put it in this folder, next to this file, then run this again.
  goto :fail
)
if not defined WD (
  echo  Missing: windowsdesktop-runtime-10.0.12-win-x64.zip
  echo  Put it in this folder, next to this file, then run this again.
  goto :fail
)

echo  Checking "%RT%" against Microsoft's checksum...
certutil -hashfile "%RT%" SHA512 | findstr /i /x "844fa99e16fd6f44e0a7c29def7a82d7846902334d6a955248a9519a4dddb3f5acceb9c9223bef69f8c83b8ae2417537e5b76dddf79fb7117dc85b5039bc1297" >nul
if errorlevel 1 (
  echo  STOPPED: this file does not match Microsoft's published checksum.
  echo  Download it again from Microsoft and try again.
  goto :fail
)
echo  OK.

echo  Checking "%WD%" against Microsoft's checksum...
certutil -hashfile "%WD%" SHA512 | findstr /i /x "8b79f679c348aa08a10ee4e1ca75be54b135ae951b7328400fea8499f9eb8450ccb18f9f708af6182b2bcd88fcca719aa282939e907b017422f519de8dd42534" >nul
if errorlevel 1 (
  echo  STOPPED: this file does not match Microsoft's published checksum.
  echo  Download it again from Microsoft and try again.
  goto :fail
)
echo  OK.

echo  Unpacking into the Runtime folder...
if not exist "Runtime" mkdir "Runtime"
tar -xf "%RT%" -C "Runtime"
if errorlevel 1 goto :unpackfail
tar -xf "%WD%" -C "Runtime"
if errorlevel 1 goto :unpackfail

if not exist "Runtime\dotnet.exe" goto :unpackfail
if not exist "Runtime\shared\Microsoft.WindowsDesktop.App\10.0.12\PresentationFramework.dll" goto :unpackfail

echo.
echo  Done. You can now open VisualInventory.exe.
echo  The two zip files can be deleted from this folder.
goto :done

:unpackfail
echo  STOPPED: the files could not be unpacked. Nothing else was changed.
goto :fail

:fail
echo.
pause
exit /b 1

:done
echo.
pause
exit /b 0
