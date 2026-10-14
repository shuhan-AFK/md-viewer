Unicode True
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "FileFunc.nsh"

!define PRODUCT_NAME "MD Viewer"
!define PRODUCT_EDITION "MD Viewer 通用版"
!define PRODUCT_VERSION "2.2.0-4"
!define PRODUCT_PUBLISHER "MD Viewer"
!define PRODUCT_DIR "MD Viewer Plain"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MDViewerPlain"

Name "${PRODUCT_EDITION}"
OutFile "..\dist\MD-Viewer-2.2.0-4-Windows-x64-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT_DIR}"
InstallDirRegKey HKCU "${PRODUCT_UNINST_KEY}" "InstallLocation"
BrandingText "${PRODUCT_NAME}"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "2.2.0.4"
VIAddVersionKey /LANG=2052 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=2052 "FileDescription" "MD Viewer Windows x64 Setup"
VIAddVersionKey /LANG=2052 "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright (c) 2026"
VIAddVersionKey /LANG=2052 "FileVersion" "2.2.0.4"
VIAddVersionKey /LANG=2052 "ProductVersion" "${PRODUCT_VERSION}"

!define MUI_ABORTWARNING
!define MUI_ICON "..\assets\plain-icon.ico"
!define MUI_UNICON "..\assets\plain-icon.ico"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Section "主程序" SEC_MAIN
  SetOutPath "$INSTDIR"
  File /r "..\source\windows\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${PRODUCT_EDITION}"
  CreateShortcut "$SMPROGRAMS\${PRODUCT_EDITION}\${PRODUCT_EDITION}.lnk" "$INSTDIR\MD Viewer.exe" "" "$INSTDIR\MD Viewer.exe" 0
  CreateShortcut "$DESKTOP\${PRODUCT_EDITION}.lnk" "$INSTDIR\MD Viewer.exe" "" "$INSTDIR\MD Viewer.exe" 0

  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_EDITION}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\MD Viewer.exe"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoRepair" 1

  WriteRegStr HKCU "Software\Classes\Applications\MD Viewer Plain.exe\shell\open\command" "" '"$INSTDIR\MD Viewer.exe" "%1"'
  WriteRegStr HKCU "Software\Classes\.md\OpenWithProgids" "MDViewerPlain.Markdown" ""
  WriteRegStr HKCU "Software\Classes\MDViewerPlain.Markdown" "" "Markdown 文档"
  WriteRegStr HKCU "Software\Classes\MDViewerPlain.Markdown\DefaultIcon" "" '"$INSTDIR\MD Viewer.exe",0'
  WriteRegStr HKCU "Software\Classes\MDViewerPlain.Markdown\shell\open\command" "" '"$INSTDIR\MD Viewer.exe" "%1"'
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${PRODUCT_EDITION}.lnk"
  Delete "$SMPROGRAMS\${PRODUCT_EDITION}\${PRODUCT_EDITION}.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT_EDITION}"
  DeleteRegKey HKCU "${PRODUCT_UNINST_KEY}"
  DeleteRegKey HKCU "Software\Classes\Applications\MD Viewer Plain.exe"
  DeleteRegKey HKCU "Software\Classes\MDViewerPlain.Markdown"
  DeleteRegValue HKCU "Software\Classes\.md\OpenWithProgids" "MDViewerPlain.Markdown"
  RMDir /r "$INSTDIR"
SectionEnd
