---
name: codex-ui-diagnostic
description: Diagnose Codex Account Switcher WPF UI states that are stuck, disabled, misleading, or not connected to runtime behavior. Use when buttons such as run/switch/refresh/delete do not enable, cards stay in loading states, profile runtime state is wrong, or a UI symptom must be traced through view model, use case, infrastructure, and direct screen verification.
---

# Codex UI Diagnostic

## Overview

Use this skill to debug Codex Account Switcher UI symptoms end to end. The goal is to prove which connection broke: screen binding, presentation state, runtime state use case, operation use case, infrastructure adapter, or external Codex process.

## Workflow

1. Reproduce from the real screen first.
   - Capture the desktop with PowerShell `System.Windows.Forms` and inspect the PNG.
   - Verify the exact visible text, button enabled appearance, and active card grouping before editing.
   - Do not click a switch/run button automatically if it may close or relaunch Codex.

2. Trace UI state inward.
   - Start at `MainWindow.xaml` and `MainWindow.xaml.cs` for event bindings.
   - Check `MainWindowViewModel` and `ProfileListPresentationState` for computed button text, enabled state, active profile grouping, and status messages.
   - For usage cards, check `ProfileUsageMonitor` and `ProfileListItemViewModel`.

3. Trace behavior into use cases.
   - For switching, inspect `SwitchProfileUseCase` and tests under `tests/CodexSwitcher.Tests/Profiles`.
   - For runtime detection, inspect `GetProfileRuntimeStateUseCase`.
   - For usage loading, inspect `RefreshProfileRateLimitUseCase` and `WindowsCodexRateLimitReader`.
   - Treat `RunningUnknownProfile` as a first-class state; decide whether the UI should block, allow, or probe from stored profile credentials.

4. Add narrow diagnostics only when the code path is still ambiguous.
   - Log stage names, counts, statuses, and booleans only.
   - Never log auth bytes, tokens, full process command lines, or credential file contents.
   - Put temporary app logs under `%LOCALAPPDATA%\CodexAccountSwitcher\*.log`.
   - Make diagnostics non-fatal: logging must never change app behavior.

5. Fix the connection that is actually broken.
   - Keep the change at the layer where the wrong decision is made.
   - Do not paper over a disabled UI state if the use case would still reject the action.
   - Update both presentation tests and use case tests when changing allowed behavior.

6. Verify in three passes.
   - Run `dotnet test`.
   - Publish the WPF app and replace the root `codexUIUIUI.exe`.
   - Launch the root exe, wait for runtime/usage polling, then capture the screen again.

## Useful Commands

```powershell
dotnet test
dotnet publish .\src\CodexSwitcher.Bootstrapper\CodexSwitcher.Bootstrapper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
Copy-Item .\src\CodexSwitcher.Bootstrapper\bin\Release\net10.0-windows\win-x64\publish\CodexSwitcher.exe .\codexUIUIUI.exe -Force
```

Screen capture:

```powershell
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp=New-Object System.Drawing.Bitmap $bounds.Width,$bounds.Height
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($bounds.Location,[System.Drawing.Point]::Empty,$bounds.Size)
$path='C:\tmp\codex-ui-diagnostic.png'
$bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
$path
```

## Safety Notes

- Full process command lines can expose sensitive arguments; avoid reading them unless the user explicitly approves after risk disclosure.
- Switching profiles may force-close Codex. Use automated click tests only when the user explicitly wants the actual switch attempted.
- If screen capture or process inspection is blocked by approvals or usage limits, report the exact blocked verification and leave reproducible commands.
