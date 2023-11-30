; andyScrSaver.nsi
;
; This script is perhaps one of the simplest NSIs you can make. All of the
; optional settings are left to their default settings. The installer simply 
; prompts the user asking them where to install, and drops a copy of example1.nsi
; there. 

; 2023, cleaning up some extra stuff.

;--------------------------------

; The name of the installer
Name "Andys Smugmug screensaver"

; The file to write
OutFile "andysScreensaverInstaller_small.exe"

; The default installation directory
InstallDir $PROGRAMFILES32\andyScrSaver


; Request application privileges for Windows Vista
RequestExecutionLevel admin

;--------------------------------

; Pages

Page directory
Page instfiles
;--------------------------------
; The stuff to install
Section "Install Application" ;No components page, name is not important

  ; Set output path to the installation directory.
  SetOutPath $INSTDIR

;Do some early cleanup
	 ;remove from system directory (Screen saver location)
  	Delete c:\Windows\System32\andyScrSaver.scr
	Delete c:\Windows\System32\andyScrSaver.scr.config

	 ;remove from system directory (Screen saver location)
  	Delete c:\Windows\Syswow64\andyScrSaver.scr
	Delete c:\Windows\Syswow64\andyScrSaver.scr.config
	Delete c:\Windows\SysWow64\screenSaverStarter.scr
	Delete c:\Windows\Syswow64\screenSaverStarter.dll

  

  ; Put file there
	File /r "C:\Users\aholk\dev\smugScreensaver\andyScreenSaver\bin\Release\*.exe"
	File /r "C:\Users\aholk\dev\smugScreensaver\andyScreenSaver\bin\Release\*.dll"
	File /r "C:\Users\aholk\dev\smugScreensaver\andyScreenSaver\bin\Release\*.config"
	File /r "C:\Users\aholk\dev\smugScreensaver\andyScreenSaver\bin\Release\*.xml"

	; include Screensaver starter files
	File /r "C:\Users\aholk\dev\smugScreensaver\ScreensaverStarter\bin\Release\net8.0-windows\*.dll"
	File /r "C:\Users\aholk\dev\smugScreensaver\ScreensaverStarter\bin\Release\net8.0-windows\*.json"
	File /r "C:\Users\aholk\dev\smugScreensaver\ScreensaverStarter\bin\Release\net8.0-windows\*.exe"
  
  CreateDirectory "$SMPROGRAMS\andySlideShow"
  CreateShortCut "$SMPROGRAMS\andySlideShow\slideshow.lnk" "$INSTDIR\andyScrSaver.exe" "" "$INSTDIR\andyScrSaver.exe" 0
  CreateShortCut "$SMPROGRAMS\andySlideShow\config.lnk" "$INSTDIR\andyScrSaver.exe" "/c" "$INSTDIR\andyScrSaver.exe" 0

WriteUninstaller "bt-uninst.exe"


  
SectionEnd ; end the section

Section "install screensaver"
SetOutPath $INSTDIR

File .\manual_screensaver_install.bat

; remove next line if you no longer run screen saver stuff.
execwait $INSTDIR\manual_screensaver_install.bat
delete .\manual_screensaver_install.bat


SectionEnd

Section "Setup"

IfFileExists $TEMP\smugmug.dat file_found file_not_found

file_not_found:

FileOpen $0 "$TEMP\smugmug.dat" w
FileWrite $0 "This file indicates that andys screensaver has been installed, delete the file to trigger configuration on next install"
FileClose $0
;MessageBox MB_OK "If this is your first install, please run configuration before starting."
MessageBox MB_YESNO "Do you wish to run configuration?" IDYES somelabel       
   Goto restofstuff
somelabel:
   ExecWait '$INSTDIR\andyscrSaver /c'
restofstuff:
file_found:
;do-nothing
SectionEnd


UninstallText "This will uninstall andy Screen saver. Hit next to continue."
UninstallIcon "${NSISDIR}\Contrib\Graphics\Icons\nsis1-uninstall.ico"


Section "Uninstall"

	; remove all files from program Files directory
    Delete "$PROGRAMFILES32\andyScrSaver\*"

  ;remove installation directory
    RMDir "$PROGRAMFILES32\andyScrSaver"
  
  ;remove links from start menu
    Delete "$SMPROGRAMS\andySlideShow\slideshow.lnk" 
	Delete "$TEMP\smugmug.dat" 
    Delete "$SMPROGRAMS\andySlideShow\config.lnk"
	
    Delete "$SMPROGRAMS\andySlideShow\Borderless slideshow.lnk" 
    RMDir "$SMPROGRAMS\andySlideShow"

SectionEnd
