# SysMonitor

A lightweight Windows taskbar system monitor that displays CPU, memory, and network speeds in real time.

Lightweight Windows taskbar system monitor that shows CPU, memory, and network speed in real-time.

![screenshot](https://img.shields.io/badge/platform-Windows-blue)
![screenshot](https://img.shields.io/badge/.NET-4.8-green)
![screenshot](https://img.shields.io/badge/size-11KB-brightgreen)

## Features

- **Taskbar embed** — sits directly in the Windows taskbar, no floating window
- **CPU & Memory** — real-time usage percentage
- **Network speed** — upload/download speed with auto-scaled units (K/M)
- **Transparent background** — blends seamlessly into the taskbar
- **Color presets** — right-click to switch between 4 color schemes
- **Single executable** — ~11KB, no dependencies, run directly

## 功能

- **嵌入任务栏** — 直接放在任务栏内，不占桌面空间
- **CPU 和内存** — 实时显示使用率百分比
- **网络速度** — 上传/下载速度，自动换算 K/M 单位
- **透明背景** — 与任务栏融为一体
- **颜色预设** — 右键菜单切换 4 种配色
- **单文件** — 仅 ~11KB，无需安装直接运行

## Usage / 使用方法

1. Download `SysMonitor.exe` from [Releases](https://github.com/freetool1002/sysmonitor/releases)
2. Run it — it appears at the left side of the taskbar
3. Right-click for color options and exit

## Build / 编译

```powershell
csc /target:winexe /out:SysMonitor.exe SysMonitor.cs
```

Requires .NET Framework 4.x (built into Windows). No SDK or NuGet needed.

## License

MIT
