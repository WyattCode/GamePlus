# Custom NSIS hooks for the Game+ installer (included by electron-builder via `nsis.include`).
#
# Why this file exists: Game+ registers a highest-privileges logon scheduled task
# (\Game+\AutoStart) that runs Game+.exe. That is only safe when Game+.exe lives in an
# admin-only-writable folder (Program Files). Older builds installed per-user into
# %LOCALAPPDATA%\Programs\Game+, where any user-level process could replace the exe and gain
# admin at next logon — so we must make sure no stale task survives such an upgrade.
#
# electron-builder already uninstalls the old per-user copy during a per-machine install
# (its uninstallOldVersion step checks HKEY_CURRENT_USER), so we do NOT duplicate that here.

# Set to "1" when an older per-user install was detected, "0" otherwise.
# Only the installer build references this variable; declaring it in the uninstaller build would
# trigger makensis warning 6001 (unreferenced variable), which is fatal because electron-builder
# runs makensis with -WX.
!ifndef BUILD_UNINSTALLER
Var gpOldPerUser
!endif

# Runs before the installer UI / before any install work.
!macro customInit
  # Look for a per-user install of Game+ (HKCU uninstall entry written by the old per-user
  # installer). A non-empty UninstallString means the old copy is present.
  ReadRegStr $0 HKCU "${UNINSTALL_REGISTRY_KEY}" "UninstallString"
  !ifdef UNINSTALL_REGISTRY_KEY_2
  ${If} $0 == ""
    # electron-builder's own uninstallOldVersion also checks the second legacy key.
    ReadRegStr $0 HKCU "${UNINSTALL_REGISTRY_KEY_2}" "UninstallString"
  ${EndIf}
  !endif
  ${If} $0 == ""
    StrCpy $gpOldPerUser "0"
  ${Else}
    StrCpy $gpOldPerUser "1"
  ${EndIf}
!macroend

# Runs after the new (per-machine) files have been installed.
!macro customInstall
  ${If} $gpOldPerUser == "1"
    # The old autostart task may point at the old user-writable exe path. Delete it; the user can
    # re-enable 开机启动 in Settings once the new install is running.
    DetailPrint "Removing stale Game+ autostart task from a previous per-user install..."
    nsExec::Exec '"$SYSDIR\schtasks.exe" /Delete /TN "\Game+\AutoStart" /F'
    Pop $0
  ${EndIf}
!macroend

# Runs during uninstall. On an update (${isUpdated}) the task is kept so 开机启动 survives the
# upgrade; on a real uninstall the task is removed.
!macro customUnInstall
  ${ifNot} ${isUpdated}
    DetailPrint "Removing Game+ autostart task..."
    nsExec::Exec '"$SYSDIR\schtasks.exe" /Delete /TN "\Game+\AutoStart" /F'
    Pop $0
  ${endif}
!macroend
