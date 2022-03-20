; andyScrSaver.nsi
;
; This script is perhaps one of the simplest NSIs you can make. All of the
; optional settings are left to their default settings. The installer simply 
; prompts the user asking them where to install, and drops a copy of example1.nsi
; there. 

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
;Var /Global BLA
;--------------------------------
; The stuff to install
Section "Install Application" ;No components page, name is not important

 ;   StrCpy $BLA "HELLO WORLD";"C:\Users\aholk\dev\smugScreensaver\andyScreenSaver\bin\Release"
	;DetailPrint "${BLA}"
  ; Set output path to the installation directory.
  SetOutPath $INSTDIR

;Do some early cleanup
	 ;remove from system directory (Screen saver location)
  	Delete c:\Windows\System32\andyScrSaver.scr
	Delete c:\Windows\System32\andyScrSaver.scr.config
	Delete c:\Windows\System32\JSONDotNET.dll
	Delete c:\Windows\System32\Newtonsoft.Json.dll
	Delete c:\Windows\System32\Newtonsoft.Json.xml
	Delete c:\Windows\System32\setupApp.exe
	Delete c:\Windows\System32\setupApp.exe.config
	Delete c:\Windows\System32\SMEngine.dll
	Delete c:\Windows\System32\SMEngine.dll.config
	Delete c:\Windows\System32\SmugMugModel.dll
	Delete c:\Windows\System32\smUtility.dll

	 ;remove from system directory (Screen saver location)
  	Delete c:\Windows\Syswow64\andyScrSaver.scr
	Delete c:\Windows\Syswow64\andyScrSaver.scr.config
	Delete c:\Windows\Syswow64\JSONDotNET.dll
	Delete c:\Windows\Syswow64\Newtonsoft.Json.dll
	Delete c:\Windows\Syswow64\Newtonsoft.Json.xml
	Delete c:\Windows\Syswow64\setupApp.exe
	Delete c:\Windows\Syswow64\setupApp.exe.config
	Delete c:\Windows\Syswow64\SMEngine.dll
	Delete c:\Windows\Syswow64\SMEngine.dll.config
	Delete c:\Windows\Syswow64\SmugMugModel.dll
	Delete c:\Windows\Syswow64\smUtility.dll

  

  ; Put file there
	File /r "C:\Users\aholk\dev\smugScreensaver\andyScreenSaver\bin\Release\*.exe"
	File /r "C:\Users\aholk\dev\smugScreensaver\andyScreenSaver\bin\Release\*.dll"
	File /r "C:\Users\aholk\dev\smugScreensaver\andyScreenSaver\bin\Release\*.config"
	File /r "C:\Users\aholk\dev\smugScreensaver\andyScreenSaver\bin\Release\*.xml"

  
  CreateDirectory "$SMPROGRAMS\andySlideShow"
  CreateShortCut "$SMPROGRAMS\andySlideShow\slideshow.lnk" "$INSTDIR\andyScrSaver.exe" "" "$INSTDIR\andyScrSaver.exe" 0
  CreateShortCut "$SMPROGRAMS\andySlideShow\config.lnk" "$INSTDIR\andyScrSaver.exe" "/c" "$INSTDIR\andyScrSaver.exe" 0
 ; CreateShortCut "$SMPROGRAMS\andySlideShow\Borderless slideshow.lnk" "$INSTDIR\andyScrSaver.exe" "/s" "$INSTDIR\andyScrSaver.exe" 0




  

WriteUninstaller "bt-uninst.exe"


  
SectionEnd ; end the section

Section "install screensaver"
SetOutPath $INSTDIR

File .\manual_screensaver_install.bat

; no longer run screen saver stuff.
; execwait $INSTDIR\manual_screensaver_install.bat
delete .\manual_screensaver_install.bat


SectionEnd

Section "Setup"

IfFileExists $TEMP\smugmug.dat file_found file_not_found

file_not_found:

;ExecWait '$INSTDIR\andyscrSaver /c'
file_found:
;MessageBox MB_OK "File found"

SectionEnd


UninstallText "This will uninstall andy Screen saver. Hit next to continue."
UninstallIcon "${NSISDIR}\Contrib\Graphics\Icons\nsis1-uninstall.ico"


Section "Uninstall"

	; remove all files from program Files directory
    Delete "$PROGRAMFILES32\andyScrSaver\*"

	 ;remove from system directory (Screen saver location)
  	Delete c:\Windows\System32\andyScrSaver.scr
	Delete c:\Windows\System32\andyScrSaver.scr.config
	Delete c:\Windows\System32\JSONDotNET.dll
	Delete c:\Windows\System32\Newtonsoft.Json.dll
	Delete c:\Windows\System32\Newtonsoft.Json.xml
	Delete c:\Windows\System32\setupApp.exe
	Delete c:\Windows\System32\setupApp.exe.config
	Delete c:\Windows\System32\SMEngine.dll
	Delete c:\Windows\System32\SMEngine.dll.config
	Delete c:\Windows\System32\SmugMugModel.dll
	Delete c:\Windows\System32\smUtility.dll
	;Delete "%TEMP%\smugmug.dat"

  ;remove installation directory
    RMDir "$PROGRAMFILES32\andyScrSaver"
  
  ;remove links from start menu
    Delete "$SMPROGRAMS\andySlideShow\slideshow.lnk" 
    Delete "$SMPROGRAMS\andySlideShow\config.lnk"
	
    Delete "$SMPROGRAMS\andySlideShow\Borderless slideshow.lnk" 
    RMDir "$SMPROGRAMS\andySlideShow"

  

SectionEnd
