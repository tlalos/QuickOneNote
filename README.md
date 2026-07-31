# QuickOneNote

A tiny Windows tray app that sends your **current selection** (text and/or images) to
OneNote with a single global hotkey — no need to switch windows.

## Why a hotkey instead of a right-click menu?

Windows does **not** let an app add an item to the right-click menu that appears when you
select text *inside another application* (Word, Chrome, Notepad…). Each app draws that menu
itself, and Windows 11's modern context menu can't be extended for it either. A global
hotkey works everywhere and is faster in practice.

For **files**, Windows *does* allow custom right-click entries, so an optional
"Add to OneNote" entry for image/`.txt` files is included (see below).

## How it works — two hotkeys

There are **two** hotkeys, because text and images are captured differently:

| Hotkey | Default | Use for | What it does |
| --- | --- | --- | --- |
| **Selection** | **Ctrl+Shift+N** | Text | Simulates Ctrl+C to copy the current selection, then appends it. |
| **Clipboard** | **Ctrl+Shift+M** | Images / screenshots | Appends whatever is already on the clipboard as-is (no copy). |

### Capturing text
1. Select the text in any app (drag over it).
2. Press **Ctrl+Shift+N**. Done.

### Capturing an image
An image can't be "text-selected", so first get it onto the clipboard, then send it:
1. Take a screenshot with **Win+Shift+S** (drag a region — it goes to the clipboard), **or**
   right-click an image → **Copy image**.
2. Press **Ctrl+Shift+M**. Done.

Images are embedded directly into the OneNote page as PNG. Your previous clipboard is
restored after a text capture.

## Requirements

- Windows 10/11
- **Desktop OneNote** (the app talks to it via COM automation).
  The old "OneNote for Windows 10" UWP app does **not** expose COM — install the desktop
  OneNote if section loading fails.
- .NET 9 SDK to build; the x86 .NET 9 Desktop Runtime to run (see below).

## Why x86 + a generated interop DLL

Two Office/OneNote quirks shape the build:

1. **x86.** The installed Office/OneNote is 32-bit and only registers its 32-bit type
   library, so the app **must run as x86** (`<PlatformTarget>x86</PlatformTarget>`) to
   marshal OneNote's COM interface. A 64-bit build fails with *"Library not registered"*.
2. **Early-bound interop.** Late-bound reflection over OneNote's `IDispatch` throws
   `TYPE_E_LIBNOTREGISTERED` (the type library is embedded as a resource inside
   `ONENOTE.EXE`, which the CLR can't resolve at runtime). So the project references a
   strongly-typed interop assembly at [lib/Microsoft.Office.Interop.OneNote.dll](lib/).

   That DLL was generated once with `tlbimp` from the embedded type library. If you need to
   regenerate it (different OneNote install path):

   ```powershell
   & "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\TlbImp.exe" `
     "C:\Program Files (x86)\Microsoft Office\Root\Office16\ONENOTE.EXE\3" `
     /out:lib\Microsoft.Office.Interop.OneNote.dll `
     /namespace:Microsoft.Office.Interop.OneNote
   ```

   (The `\3` is the type-library resource index inside the exe.)

## Build & run

```bash
dotnet build -c Release
```

Then run the produced `bin\Release\net9.0-windows\QuickOneNote.exe` (a 32-bit app). A purple
**N** icon appears in the system tray.

To produce a single self-contained `.exe`:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## First-time setup

On first launch the **Settings** window opens automatically:

- **OneNote section** — pick where notes go (list is read live from OneNote).
- **When capturing**:
  - *Append to today's page* — everything goes to one page named with today's date,
    each entry timestamped.
  - *Create a new page each time* — a fresh page per capture.
- **Selection hotkey** (text) and **Clipboard hotkey** (images) — click each box and press
  your preferred combination (each must include Ctrl, Shift, or Alt, and they must differ).

Settings are stored in `%APPDATA%\QuickOneNote\settings.json`.

## Using it

- **Ctrl+Shift+O** — capture the current text selection.
- **Ctrl+Shift+I** — send the current clipboard (use for images/screenshots you copied).
- **Ctrl+Shift+S** — capture the **whole screen** and send it (no copy needed).
- **Ctrl+Shift+G** — **snip a region**: drag a rectangle, annotate (pen/highlighter), then
  Copy / Save / Send to OneNote. The snip is also copied to the clipboard automatically.
- **Ctrl+Shift+B** — **start/finish a screenshot series** (see below).
- The tray menu has all these actions plus **Settings…** and **Exit**.
- **Double-click** the tray icon = capture selection (same as Ctrl+Shift+O).

All four hotkeys are configurable in **Settings**.

### Snip tool (Ctrl+Shift+X)

1. Press the hotkey → the screen dims across all monitors.
2. Drag a rectangle over the area you want (a size readout follows the cursor). **Esc** or
   right-click cancels.
3. An editor opens with your crop. Use **Pen** or **Highlighter**, pick a **color**, **Undo**
   as needed.
4. Click **Send to OneNote** (or **Copy** / **Save…**). The un-annotated snip was already put
   on your clipboard when you released the mouse.

## Optional: right-click on files in Explorer

Add an "Add to OneNote" entry for image and `.txt` files (per-user, no admin needed):

```powershell
./Install-ContextMenu.ps1
```

Remove it with:

```powershell
./Uninstall-ContextMenu.ps1
```

Right-clicking a supported file and choosing **Add to OneNote** sends its contents to your
configured section.

### Screenshot series (Ctrl+Shift+B)

Capture several screenshots as one titled block:

1. **Start** a series: press **Ctrl+Shift+B** (or tray → *Start screenshot series*).
2. **Snip** each region with **Ctrl+Shift+G** — while a series is active, each snip is added to
   the series (you'll see "Added to series (N)").
3. **Finish**: press **Ctrl+Shift+B** again (or tray → *Finish series (N)…*) to open the
   **review window**.
4. In review: type a **Title**; use **Prev/Next** to move between shots; for each shot add a
   **caption** and optionally **draw** (pen/highlighter); **Delete shot** to drop one.
5. **Submit to OneNote** → you get the **title in bold**, then each shot with its **caption
   above** the (annotated) image, in order.

Tray → *Cancel series* discards an in-progress series.

## Cloud mode (no OneNote installed) — Microsoft Graph

QuickOneNote can send notes to your **cloud** OneNote via the Microsoft Graph API, so it runs
on a PC that has **no OneNote installed** (needs internet + a one-time Microsoft sign-in).

### One-time: register a free Azure app (gets you a Client ID)

Microsoft requires any app that uses Graph to have an app registration. It's free and takes a
few minutes:

1. Go to **https://entra.microsoft.com** → **Applications** → **App registrations** → **New registration**.
2. **Name:** `QuickOneNote`.
3. **Supported account types:** *Personal Microsoft accounts only*.
4. Click **Register**, then copy the **Application (client) ID** (a GUID).
5. Left menu → **Authentication** → scroll to **Advanced settings** → set
   **Allow public client flows** = **Yes** → **Save**. *(Required for device-code sign-in.)*
6. Left menu → **API permissions** → **Add a permission** → **Microsoft Graph** →
   **Delegated permissions** → check **Notes.ReadWrite** → **Add permissions**.
   *(No admin consent needed for a personal account.)*

No redirect URI or client secret is required — the app uses the OAuth **device-code** flow.

### In QuickOneNote

1. Tray icon → **Settings…**
2. Choose **Cloud — Microsoft account**.
3. Paste the **Client ID** you copied.
4. Click **Sign in…** → a code appears → the sign-in page opens → enter the code and approve.
5. Click **Refresh**, pick your target **section**, set mode/hotkeys, **Save**.

Your sign-in is remembered (the refresh token is stored **encrypted** with Windows DPAPI in
`%APPDATA%\QuickOneNote\graph_token.bin`).

### Running on the OneNote-less PC

That PC needs the **x86 .NET 9 Desktop Runtime**, or use a **self-contained** build that bundles
the whole runtime (no .NET install required on the target PC).

**Standalone folder** (recommended — copy the whole folder and run the exe):

```bash
dotnet publish -c Release -r win-x86 --self-contained true -o dist
```

This produces `dist\` (~105 MB, includes the .NET runtime). Copy the folder anywhere and run
`QuickOneNote.exe`.

**Single-file exe** (one file instead of a folder):

```bash
dotnet publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true
```

Either way, pick **Cloud** in Settings on a PC without OneNote installed.

## Run at startup (optional)

Put a shortcut to `QuickOneNote.exe` in your Startup folder
(`shell:startup` in the Run dialog) to launch it with Windows.

## Notes & limits

- Text keeps line breaks but not rich formatting (plain text is used for reliability).
- If the hotkey can't be registered, another app is probably using it — pick another in
  Settings.
- The clipboard is restored after each capture on a best-effort basis (text, HTML, image,
  and file lists).
