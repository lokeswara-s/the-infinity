# Contributing

Thanks for helping with The Infinity. Bug reports, small fixes and focused features are all welcome.

## Set up

You need Windows 10 (1809) or later and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/<your-account>/the-infinity.git
cd the-infinity
dotnet build TheInfinity.slnx
dotnet test TheInfinity.slnx
dotnet run --project src/TheInfinity.App
```

## How the code is split

| Project | Responsibility | May reference |
| --- | --- | --- |
| `TheInfinity.Core` | Viewport math, snapping, the workspace session, window matching. No Windows APIs, fully unit tested. | nothing |
| `TheInfinity.Win32` | `EnumWindows`, `SetWinEventHook`, `SetWindowPos`, DWM thumbnails, the global mouse hook. | Core |
| `TheInfinity.Persistence` | SQLite storage and migrations. | Core |
| `TheInfinity.App` | WPF shell, view models, tray, hotkeys, the dependency injection setup. `CanvasController` ties things together and delegates to small helpers in `Services/` (autosave, window events, global mouse input, animation). Drag and click gestures live in the window code-behind on purpose. | all of the above |

New behaviour should go into `Core` first so it can be tested without a desktop.

## Ground rules

These keep the app safe to run next to everything else on someone's machine.

- Only documented Win32 and DWM APIs. No undocumented calls, no code injection, no administrator requirement.
- Never persist window handles (`HWND`). Match windows again by process path, class and title.
- Never move a window to extreme off-screen coordinates. Windows that are off the screen are minimized instead.
- Do not start other programs unless the user turned that setting on.
- Every hook, hotkey and thumbnail that is registered must be released on shutdown.

## Before you open a pull request

- `dotnet build TheInfinity.slnx` has no warnings and `dotnet test TheInfinity.slnx` passes.
- Add or update a test when you change `Core`, `Persistence` or `Win32`. `TheInfinity.Win32.Tests` opens a few real windows without taking focus. Set `INFINITY_SKIP_DESKTOP_TESTS=1` to skip those.
- If you touch hooks, the overview or the controller, go through [docs/manual-testing.md](docs/manual-testing.md).
- For UI changes, attach a screenshot. `TheInfinity.exe --render-ui <folder>` writes PNGs of the command bar and the settings window without touching your desktop.
- Keep the change focused and describe how you tried it.

By contributing you agree that your work is released under the [MIT license](LICENSE).
