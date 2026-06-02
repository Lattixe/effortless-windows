# Effortless Windows

A minimalist task timer for Windows. Focus on what matters.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/Platform-Windows-0078D6)
![License](https://img.shields.io/badge/License-MIT-green)

> Windows clone of the excellent [Effortless](https://github.com/Lattixe/effortless) macOS app

## Features

- **Floating Timer Widget** - Always-visible countdown in the corner of your screen
- **Text-Based Task List** - One task per line, simple and distraction-free
- **Smart Timer Parsing** - Add a number at the end to set minutes (e.g., `Deep work 45`)
- **Dark Mode UI** - Pure black "lights out" design, easy on the eyes
- **Scratch Pad** - Quick notes that persist across sessions
- **Slash Commands** - Turn a scratch-pad line into a task (`/read 30`) or vault a thought (`/vault`)
- **Thought Vault** - Capture an idea, file it as an agent-native markdown wiki, start fresh
- **Global Hotkeys** - Control everything without touching your mouse
- **System Tray** - Lives quietly in your notification area

## Screenshot

```
┌─────────────────────────┐
│ Deep work    43:21      │  ← Floating timer widget
└─────────────────────────┘

┌─────────────────────────────┐
│ Effortless              ×   │
├─────────────────────────────┤
│                             │
│ Deep work 45                │  ← Task list editor
│ Review PRs 30               │
│ Team standup 15             │
│ Email cleanup               │
│                             │
└─────────────────────────────┘
```

## Hotkeys

| Shortcut | Action |
|----------|--------|
| `Shift+Alt+L` | Toggle task list |
| `Shift+Alt+P` | Toggle scratch pad |
| `Shift+Alt+V` | Vault the current scratch-pad thought |
| `Ctrl+Alt+D` | Mark current task done |
| `Ctrl+Alt+R` | Add 5 minutes to timer |
| `Ctrl+Alt+Space` | Pause/Resume timer |
| `Esc` | Close current window |

Inside the scratch pad: `Ctrl` `+` / `Ctrl` `-` adjust the font size, `Ctrl` `0` resets it.

## How It Works

1. Press `Ctrl+Alt+L` to open the task list
2. Type your tasks, one per line
3. Add a number at the end to set a timer in minutes:
   ```
   Deep work 45
   Review PRs 30
   Quick break
   ```
4. The first task becomes active with its countdown shown in the floating widget
5. Press `Ctrl+Alt+D` when done to move to the next task
6. Timer notifications alert you when time's up

## Scratch Pad & Slash Commands

Open the scratch pad with `Ctrl+Alt+P` for free-form capture. At the start of any
line, type a slash command and press **Enter**:

| Command | Action |
|---------|--------|
| `/<task> <minutes>` | Add a task to the timer queue, e.g. `/read 30`. The line becomes `- [ ] read 30`. |
| `/vault` | File the whole pad as a thought in the vault, then clear the pad for a fresh idea. |
| `/vault <title>` | Same, but with an explicit title instead of the auto-derived one. |

The scratch-pad title bar also has **⬇ Vault**, **☰ Vault Browser**, and an
open-folder button.

## Thought Vault

The vault turns fleeting scratch-pad ideas into a durable, **agent-native markdown
knowledge base** — a personal "Karpathy wiki" you can prompt against.

- Each vaulted thought is a standalone `.md` file with YAML frontmatter
  (`title`, `id`, `created`, `tags`).
- An `index.md` catalog is regenerated automatically on every change.
- A `README.md` explains the structure to any AI agent you point at the folder.
- Everything lives in `Documents\Effortless Vault\` so it's easy to find, sync,
  back up, and query.

```
Documents\Effortless Vault\
├── README.md          ← how to use / query the vault (for humans and agents)
├── index.md           ← auto-generated catalog of every thought
└── thoughts\
    └── 2026-06-02-product-catalog.md
```

Because it's plain markdown, you can point Claude Code (or any LLM) at the folder
and ask things like *"read index.md and summarize the themes across my thoughts"*
or *"turn the thought titled X into a spec."*

## Installation

### Quick Install (Recommended)

1. Clone or download this repo
2. Run the install script:
   ```powershell
   powershell -ExecutionPolicy Bypass -File install.ps1
   ```

This will:
- Install Effortless to `%LOCALAPPDATA%\Effortless`
- Add "Effortless" to your Start Menu (searchable)
- Set Effortless to run automatically on Windows startup
- Launch the app immediately

### Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File uninstall.ps1
```

Your tasks and notes are preserved after uninstall.

### Build from Source

```bash
git clone https://github.com/Lattixe/effortless-windows.git
cd effortless-windows
dotnet publish -c Release -r win-x64 --self-contained -o publish
powershell -ExecutionPolicy Bypass -File install.ps1
```

## Data Storage

Tasks and the active scratch pad are saved to:
```
%LOCALAPPDATA%\Effortless\
├── tasks.json
└── ScratchPad\current.md
```

Vaulted thoughts live in your Documents folder so they're easy to find and sync:
```
Documents\Effortless Vault\
├── README.md
├── index.md
└── thoughts\*.md
```

## Philosophy

> No hierarchies. No tags. No labels. No checkboxes. Just text.

Effortless is intentionally minimal. It's not a project management tool or a complex task system. It's a simple timer that helps you focus on one thing at a time.

## Tech Stack

- **C# / .NET 8** - Modern, fast, native Windows
- **WPF** - Windows Presentation Foundation for UI
- **Windows Forms** - NotifyIcon for system tray
- **Windows API** - RegisterHotKey for global shortcuts

## Credits

- Original macOS app: [Effortless](https://github.com/Lattixe/effortless) by Lattixe
- Windows port built with assistance from Claude

## License

MIT License - See [LICENSE](LICENSE) for details
