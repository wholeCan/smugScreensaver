@echo off
echo  *******************  MAKE SURE YOU RUN THIS AS ADMIN AFTER RUNNING INSTALLER ****
echo *********************************************************************************

copy "C:\program files (x86)\andyScrSaver\andyScrSaver.exe" "C:\windows\sysWow64\andyScrSaver.scr"
copy "C:\program files (x86)\andyScrSaver\andyScrSaver.exe.config" "C:\windows\SysWow64\andyScrSaver.scr.config"
copy "C:\Program Files (x86)\andyScrSaver\andyScrSaver.exe.manifest" "C:\windows\SysWow64\andyScrSaver.scr.manifest"
copy "C:\Program Files (x86)\andyScrSaver\*.dll"  C:\windows\SysWow64\
copy "C:\Program Files (x86)\andyScrSaver\*.xml"  C:\windows\SysWow64\
copy "C:\Program Files (x86)\andyScrSaver\*.config"  C:\windows\SysWow64\



