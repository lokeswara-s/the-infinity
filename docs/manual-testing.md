# Manual test checklist

The automated tests cover the logic in `Core`, the SQLite store, and the Win32 calls that can be tried on a window the test creates. These things need a person at a desktop. Run through them before a release, or after touching the hooks, the overview, or the controller.

Use at least three ordinary windows (for example Notepad, a browser and File Explorer) plus one Store app such as Calculator.

## Start and stop

- [ ] The app starts on the normal desktop at 100% and the tray icon appears.
- [ ] Exit from the tray. Start again. Windows come back where they were.
- [ ] Kill the process in Task Manager. On the next start the layout is restored and a notice is shown.

## Zoom and pan

- [ ] `Ctrl+Alt` + scroll down enters the overview. All windows show live thumbnails, including the Store app.
- [ ] `Ctrl+Alt` + scroll up returns to 100% and stops at 100%.
- [ ] `Ctrl+Alt` + middle-drag pans from inside another app. A plain middle-click in that app still works afterwards.
- [ ] In the overview, plain middle-drag and dragging empty space both pan.
- [ ] `Ctrl+Alt+O` and `Esc` toggle the overview and return to exactly the same view.
- [ ] `Ctrl+Alt+H` goes home and `Ctrl+Alt+F` fits every window.

## Windows

- [ ] Click a thumbnail: it zooms to real size where it is and has keyboard focus.
- [ ] Drag a thumbnail, zoom back in, and the real window is where you dropped it.
- [ ] Two thumbnails snap to each other's edges. Hold nothing; just drag close.
- [ ] Minimize a window, then look at it in the overview. It still shows a preview (live or a snapshot).
- [ ] Close an app. Its tile stays as a gold "Not running" outline. Reopen the app and it snaps back. "Forget this window" removes the tile.
- [ ] Right-click a thumbnail and try focus, minimize, maximize, close and pin.

## Command bar

- [ ] Search finds a window by title and by app. Enter zooms to the first match.
- [ ] Layouts: save a layout named "A", move things, save "B", click "A" and the arrangement returns. Saving "A" again replaces it. The delete button removes it.
- [ ] The gear opens settings. Changing a setting and pressing Save takes effect without a restart.

## Odd setups

- [ ] Two monitors, with the second one to the left of the first (negative coordinates).
- [ ] Display scaling of 150% on one monitor and 100% on the other.
- [ ] An elevated window (run Notepad as administrator): the app should log a warning and carry on.
- [ ] Unplug a monitor while windows are on it.
