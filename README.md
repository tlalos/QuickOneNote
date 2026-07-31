# QuickOneNote

A lightweight **Windows tray app** that sends text, images, and screenshots to **OneNote** with
global hotkeys — plus a built‑in snip tool with annotations, a "stunning image" beautifier, and
offline OCR. Works with **local desktop OneNote** (COM) or **cloud OneNote** (Microsoft Graph, so
it runs on a PC with no OneNote installed).

> Why hotkeys and not a right‑click menu? Windows doesn't let apps add items to the text‑selection
> context menu inside other apps (each app draws its own, and the Win11 modern menu can't be
> extended). A global hotkey works everywhere and is faster.

## Features

**Capture (global hotkeys)**
- **Selected text** → OneNote (simulated copy) — `Ctrl+Shift+O`
- **Clipboard image / screenshot** → OneNote — `Ctrl+Shift+I`
- **Full focused monitor** screenshot → OneNote — `Ctrl+Shift+S`
- **Region snip** with an editor — `Ctrl+Shift+G`
- **Screenshot series** (batch) — `Ctrl+Shift+B`

**Snip tool**
- Floating Snipping‑Tool‑style bar (capture, **eyedropper color‑picker**, close) with a live
  color readout.
- Editor with annotations: **select/move**, **pen**, **highlighter**, **shapes**
  (rectangle / ellipse / line / arrow), **text**, **blur / redact** (pixelate), **numbered
  steps**, colors + custom color, thickness, **undo/redo** (Ctrl+Z / Ctrl+Y).
- **Beautifier** — frame the shot on a **gradient/solid background** with **padding**,
  **rounded corners**, **drop shadow**, and **aspect presets** (Auto / 16:9 / Square / Story).
- **OCR** — extract text from the screenshot **offline** (built‑in Windows OCR), then copy it or
  send it to OneNote.
- Copy to clipboard, Save as PNG, Send, or **Send with a title** (a new titled section).

**Screenshot series**
- Snip several regions, then a **review window**: give the set a **title**, page through shots,
  add a **caption** and annotations per shot. Sends the **bold title** followed by each shot with
  its caption above the image.

**Backends**
- **Local** desktop OneNote via COM (no sign‑in, works offline).
- **Cloud** via Microsoft Graph (device‑code sign‑in; **no OneNote install needed**). The refresh
  token is stored **encrypted** with Windows DPAPI.

**Quality‑of‑life**
- Append to **today's page** or a **new page** each time; entries separated and titled.
- Fully **configurable hotkeys** (clear one to disable it), notification toggle, and
  **Start with Windows**.
- **Single instance** / single editor window.
- Optional **"Add to OneNote"** right‑click entry for image/`.txt` files in Explorer.
- Ships as a **self‑contained** build that needs no .NET install on the target PC.

## Hotkeys (defaults, all configurable in Settings)

| Hotkey | Action |
| --- | --- |
| `Ctrl+Shift+O` | Send selected **text** |
| `Ctrl+Shift+I` | Send **clipboard** (copied image / screenshot) |
| `Ctrl+Shift+S` | **Full‑screen** shot of the focused monitor |
| `Ctrl+Shift+G` | **Snip a region** → annotate / beautify / OCR → send |
| `Ctrl+Shift+B` | **Start / finish a screenshot series** |

The tray menu exposes every action plus **Settings…** and **Exit**; double‑clicking the tray icon
captures the selection.

## Requirements

- Windows 10/11
- **Local mode** needs the **desktop OneNote** app (COM automation). The old "OneNote for
  Windows 10" UWP app doesn't expose COM. **Cloud mode** needs no OneNote install.
- To build: **.NET 9 SDK**. To run a framework‑dependent build: the **x86 .NET 9 Desktop Runtime**
  (or use the self‑contained build below).

## Build & run

```bash
dotnet build -c Release
```

Run the produced `bin\Release\net9.0-windows10.0.19041.0\QuickOneNote.exe` (a 32‑bit app). A purple
**N** icon appears in the system tray.

**Standalone build** that bundles the whole runtime (no .NET needed on the target PC):

```bash
dotnet publish -c Release -r win-x86 --self-contained true -o dist
```

Copy the `dist\` folder anywhere and run `QuickOneNote.exe`.

### Why x86 + a generated interop DLL

- **x86:** desktop Office/OneNote is 32‑bit and only registers its 32‑bit type library, so the app
  must run as **x86** to marshal OneNote's COM interface (a 64‑bit build fails with *"Library not
  registered"*).
- **Early‑bound interop:** late binding over OneNote's `IDispatch` throws
  `TYPE_E_LIBNOTREGISTERED` (its type library is embedded in `ONENOTE.EXE`), so the project
  references a strongly‑typed interop at [`lib/Microsoft.Office.Interop.OneNote.dll`](lib/),
  generated once with `tlbimp` from the embedded type library.
- **OCR** uses the WinRT `Windows.Media.Ocr` API, so the project targets
  `net9.0-windows10.0.19041.0`.

## Cloud mode — Microsoft Graph (no OneNote install)

Register a **free Azure app** to get a Client ID (Microsoft requires this for any Graph app):

1. **https://entra.microsoft.com** → **App registrations** → **New registration**.
2. Name `QuickOneNote`, account type **Personal Microsoft accounts only** → **Register**.
3. Copy the **Application (client) ID**.
4. **Authentication** → **Allow public client flows** = **Yes** → Save.
5. **API permissions** → Microsoft Graph → Delegated → **Notes.ReadWrite** → Add.

Then in QuickOneNote: **Settings → Cloud → paste Client ID → Sign in…** (enter the device code) →
pick a section → **Save**. No redirect URI or secret is needed (OAuth device‑code flow).

## Start at login

Tick **Start with Windows** in Settings (adds a per‑user `HKCU\…\Run` entry — no admin needed).

## Notes & limits

- Text is sent as plain text (line breaks preserved) for reliability.
- If a hotkey can't be registered, another app owns that combo — pick another in Settings, or
  clear it to disable.
- OCR quality depends on your installed Windows display languages.
- AI background removal (a paid feature in some tools) is intentionally out of scope.

## License

[MIT](LICENSE).
