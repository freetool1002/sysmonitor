# SysMonitor 项目规范

## 通信语言
- 你回答问题都用中文

## 技术栈
- 语言：C# (.NET Framework 4.x)
- 编译器：`C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe`
- 目标：`/target:winexe`，输出 `SysMonitor.exe`
- 仅支持 C# 5 语法（不支持：string interpolation、null-conditional、pattern matching、out var）

## 项目结构
- `SysMonitor.cs` — 唯一源码文件
- `SysMonitor.exe` — 编译输出（已 gitignore）
- `.github/workflows/release.yml` — CI 自动编译发布

## 分支策略
- `main` — 稳定版本，受保护，只能通过 PR 合并
- `dev` — 日常开发，功能分支从 dev 切出
- `feature/xxx` — 新功能分支
- `fix/xxx` — 修复分支
- tag `v*.*.*` 触发 GitHub Actions 自动发布 Release

## 设计原则
- 保持简洁，不引入花哨功能
- 新功能必须同步更新 README.md
- 发布版本前需经用户确认
- 任何代码修改都需经用户确认

## 编码约定
- 代码添加中文注释（2026-10-18 用户明确要求，覆盖旧规约）
- 保持单文件结构
- UI 文字用中文
- 右键菜单暗色主题
- 嵌入任务栏而非悬浮
