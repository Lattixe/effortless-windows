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
| `Ctrl+Alt+L` | Toggle task list |
| `Ctrl+Alt+P` | Toggle scratch pad |
| `Ctrl+Alt+D` | Mark current task done |
| `Ctrl+Alt+R` | Add 5 minutes to timer |
| `Ctrl+Alt+Space` | Pause/Resume timer |
| `Esc` | Close current window |

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

## Installation

### Requirements
- Windows 10/11
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build from Source

```bash
git clone https://github.com/Lattixe/effortless-windows.git
cd effortless-windows
dotnet build
dotnet run
```

### Run Release Build

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

The executable will be in `bin/Release/net8.0-windows/win-x64/publish/`

## Data Storage

Tasks and scratch pad notes are saved to:
```
%LOCALAPPDATA%\Effortless\
├── tasks.json
└── scratchpad.txt
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
