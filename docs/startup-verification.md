# Startup acceptance checks

Run these on Windows with a webcam, using both a regular build and an installed MSIX package. These are manual acceptance checks, not a record of completed UI testing.

1. Start with no settings file: startup launch is off. A manual launch leaves detection off.
2. Turn startup launch on: the consent dialog describes sign-in launch, the configured interval, the camera indicator, continuous preview, public-place risks and how to turn it off. Cancel or press Escape: no startup registration or consent is saved. Confirm: startup is registered for the current user.
3. Sign out and back in (or run the regular executable with `--startup` for a launch-flow check). A persistent modal reminder appears. Escape cannot dismiss it. The camera remains off until OK. Click OK: detection starts at the configured interval with the window hidden in the tray. Without a webcam or permission, detection stays off and the error is visible.
4. Hold the reminder's OK button for less than five seconds, then release: ordinary acknowledgment; later startup reminders still appear. Press, then move outside the button or cancel the pointer: the hold is canceled. Holding continuously inside the button for five seconds opens a separate confirmation with keeping reminders as the default.
5. Cancel the permanent-suppression confirmation: the current launch continues but future launches still warn. Confirm suppression: the preference is saved, subsequent startup launches skip the reminder and begin detection. Manual launches still leave detection off.
6. Click “恢复每次开机运行提醒” in settings: subsequent startup launches warn again. Disable and re-enable startup launch: consent is required again and reminders are restored.
7. Disable startup launch: the Windows registration is removed/disabled, while current detection is unchanged. Check that save/registration failures display an error and restore the previous preference. For MSIX, disabling in Task Manager must not be silently overridden by the app.
8. With an instance already open, launch again with `--startup`: no second detection instance starts. The existing instance displays the reminder (unless suppressed); its OK button supports the same long press. Ordinary duplicate launches just show the existing window.
9. Verify the quoted startup command works from a directory containing spaces, and portable builds register their original executable path. Moving the portable executable requires enabling startup again at its new location.
