
if exist andysScreenSaverInstaller.exe del andysScreenSaverInstaller.exe
if exist andysScreenSaverInstaller_small.exe del andysScreenSaverInstaller_small.exe
::  large installer no longer needed with modern windows.  "C:\Program Files (x86)\NSIS\makeNSIS.exe" andyScrSaver.nsi

:: Pass FAST=1 to skip compression for dev builds.
if "%1"=="FAST=1" (
    echo Building FAST
    "C:\Program Files (x86)\NSIS\makeNSIS.exe" /X"SetCompress off" andyScrSaver_small.nsi
) else (
    echo Building SLOW
    "C:\Program Files (x86)\NSIS\makeNSIS.exe" andyScrSaver_small.nsi
)
pause