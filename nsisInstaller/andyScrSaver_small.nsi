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

SetCompressor zlib

; The default installation directory
InstallDir $PROGRAMFILES32\andyScrSaver


; Request application privileges for Windows Vista
RequestExecutionLevel admin

;--------------------------------

; Use paths relative to this .nsi file location (robust regardless of CWD)
!define ROOT_DIR "${__FILEDIR__}\.."
!define APP_RELEASE "${ROOT_DIR}\andyScreenSaver\bin\Release"
!define STARTER_RELEASE "${ROOT_DIR}\ScreensaverStarter\bin\Release\net8.0-windows"

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

  

  ; Put file there (VLC libs excluded and handled separately below)
	File /r /x "libvlc*" "${APP_RELEASE}\*.exe"
	File /r /x "libvlc*" "${APP_RELEASE}\*.dll"
	File /r "${APP_RELEASE}\*.config"
	File /r "${APP_RELEASE}\*.xml"

	IfFileExists "$INSTDIR\libvlc.dll" vlc_exists vlc_missing
	vlc_missing:
		DetailPrint "Installing VLC components..."
		File /r "${APP_RELEASE}\libvlc*"
	vlc_exists:
		DetailPrint "VLC components already installed, skipping..."


	; include Screensaver starter files
	File /r "${STARTER_RELEASE}\*.dll"
	File /r "${STARTER_RELEASE}\*.json"
	File /r "${STARTER_RELEASE}\*.exe"

	; upgrade helper script (Start Menu "Upgrade slideshow" shortcut runs this)
	File ".\upgrade-slideshow.ps1"

  CreateDirectory "$SMPROGRAMS\andySlideShow"
  CreateShortCut "$SMPROGRAMS\andySlideShow\slideshow.lnk" "$INSTDIR\andyScrSaver.exe" "" "$INSTDIR\andyScrSaver.exe" 0
  CreateShortCut "$SMPROGRAMS\andySlideShow\config.lnk" "$INSTDIR\andyScrSaver.exe" "/c" "$INSTDIR\andyScrSaver.exe" 0
  CreateShortCut "$SMPROGRAMS\andySlideShow\Upgrade slideshow.lnk" "powershell.exe" '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "$INSTDIR\upgrade-slideshow.ps1"' "$INSTDIR\andyScrSaver.exe" 0

WriteUninstaller "bt-uninst.exe"


  
SectionEnd ; end the section

Section "install screensaver"
; Registers ScreensaverStarter as a native Windows screensaver by copying it
; into SysWow64 as a .scr (Windows lists any .scr found there in Display
; Settings). Previously done by manual_screensaver_install.bat; done natively
; here instead.
SetOutPath "$WINDIR\SysWow64"
CopyFiles /SILENT "$INSTDIR\screenSaverStarter.dll" "$WINDIR\SysWow64\screenSaverStarter.dll"
CopyFiles /SILENT "$INSTDIR\screenSaverStarter.deps.json" "$WINDIR\SysWow64\screenSaverStarter.deps.json"
CopyFiles /SILENT "$INSTDIR\screenSaverStarter.runtimeconfig.json" "$WINDIR\SysWow64\screenSaverStarter.runtimeconfig.json"
CopyFiles /SILENT "$INSTDIR\screenSaverStarter.exe" "$WINDIR\SysWow64\SmugAndy-slideshow.scr"
SectionEnd

Section "Setup"

IfFileExists $TEMP\smugmug.dat file_found file_not_found

file_not_found:

FileOpen $0 "$TEMP\smugmug.dat" w
FileWrite $0 "This file indicates that andys screensaver has been installed, delete the file to trigger configuration on next install"
FileClose $0
;MessageBox MB_OK "If this is your first install, please run configuration before starting."
MessageBox MB_YESNO "Do you wish to run configuration?" IDYES runConfiguration       
   Goto restofstuff
runConfiguration:
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

  ;remove cached images and image dictionary (per-user, so this only cleans up the current user)
    RMDir /r "$LOCALAPPDATA\andyScreenSaver"

  ;remove installation directory
    RMDir "$PROGRAMFILES32\andyScrSaver"

  ;remove screensaver registration from SysWow64
    Delete "$WINDIR\SysWow64\SmugAndy-slideshow.scr"
    Delete "$WINDIR\SysWow64\screenSaverStarter.dll"
    Delete "$WINDIR\SysWow64\screenSaverStarter.deps.json"
    Delete "$WINDIR\SysWow64\screenSaverStarter.runtimeconfig.json"

  ;remove links from start menu
    Delete "$SMPROGRAMS\andySlideShow\slideshow.lnk"
	Delete "$TEMP\smugmug.dat"
    Delete "$SMPROGRAMS\andySlideShow\config.lnk"
    Delete "$SMPROGRAMS\andySlideShow\Upgrade slideshow.lnk"

    Delete "$SMPROGRAMS\andySlideShow\Borderless slideshow.lnk"
    RMDir "$SMPROGRAMS\andySlideShow"

SectionEnd
