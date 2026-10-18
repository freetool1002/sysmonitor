# SysMonitor

轻量级 Windows 任务栏系统监视器，实时显示 CPU、内存和网络速度。

当前版本：V1.0

![platform](https://img.shields.io/badge/platform-Windows%207--11-blue)
![.NET](https://img.shields.io/badge/.NET-4.x-green)
![size](https://img.shields.io/badge/size-18KB-brightgreen)
![version](https://img.shields.io/badge/version-V1.0-orange)

## 支持的系统

| 系统 | 架构 | 状态 |
|------|------|------|
| Windows 7 SP1 | x86 / x64 | 支持 |
| Windows 8 | x86 / x64 | 支持 |
| Windows 8.1 | x86 / x64 | 支持 |
| Windows 10 | x86 / x64 | 支持 |
| Windows 11 | x64 | 支持 |

运行时依赖 .NET Framework 4.x（Windows 系统自带，无需额外安装）。

## 功能

- **嵌入任务栏** — 直接放在任务栏内，不占桌面空间
- **CPU 和内存** — 实时显示使用率百分比
- **网络速度** — 上传/下载速度，自动换算 K/M 单位
- **显示项开关** — 右键菜单自由勾选要显示的 CPU / 内存 / 上传 / 下载
- **网络适配器选择** — 多网卡时可指定监视的适配器，避免虚拟网卡重复计数
- **配置记忆** — 配色、显示项、适配器选择自动保存，重启后保留
- **透明背景** — 与任务栏融为一体
- **颜色预设** — 右键菜单切换 4 种配色
- **单文件** — 仅 ~18KB，无需安装直接运行
- **开机自启** — 右键菜单可设置开机自动启动
- **关于信息** — 右键菜单查看版本号与版权信息

## 使用方法

1. 从 [Releases](https://github.com/freetool1002/sysmonitor/releases) 下载 `SysMonitor.exe`
2. 直接运行 — 程序自动出现在任务栏左侧
3. 右键点击弹出菜单，可切换字体颜色、勾选显示项目、选择网络适配器、设置开机自启、查看关于或退出

## 编译

```powershell
csc /target:winexe /out:SysMonitor.exe SysMonitor.cs
```

无需 SDK 或 NuGet，Windows 自带 .NET Framework 4.x 编译器。

## 软件著作权

`软著材料/` 目录下为申请计算机软件著作权登记所需材料：

- `软件说明书.md` — 软件说明书 / 用户手册
- `源程序文档.txt` — 按每页 50 行排版的源程序文档
- `申请信息表.md` — 申请信息汇总与材料清单
- `截图/` — 说明书所用运行截图（主界面、右键菜单、显示项目子菜单、关于对话框）

## License

MIT
