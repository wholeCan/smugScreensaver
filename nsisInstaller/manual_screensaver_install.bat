@echo off
echo  *******************  MAKE SURE YOU RUN THIS AS ADMIN AFTER RUNNING INSTALLER ****
echo *********************************************************************************

copy "C:\Program Files (x86)\andyScrSaver\screenSaverStarter.dll"  C:\windows\SysWow64\
copy "C:\Program Files (x86)\andyScrSaver\screenSaverStarter.deps.json"  "C:\windows\SysWow64\"
copy "C:\Program Files (x86)\andyScrSaver\screenSaverStarter.runtimeconfig.json"  "C:\windows\SysWow64\"
copy "C:\Program Files (x86)\andyScrSaver\screenSaverStarter.exe"  "C:\windows\SysWow64\screenSaverStarter.scr"




