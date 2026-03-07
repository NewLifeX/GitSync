## GitSync 代码库同步
Git代码库同步，保持本地代码库到多个远程库之间的同步。  

[![NuGet](https://img.shields.io/nuget/v/GitSync.svg)](https://nuget.org/packages/GitSync)
[![Build Status](https://github.com/NewLifeX/GitSync/actions/workflows/publish.yml/badge.svg)](https://github.com/NewLifeX/GitSync/actions)

---

## 功能特性

- **多远程同步**：一次操作将本地仓库拉取并推送到所有配置的远程库（如 `origin`、`github`、`gitee`）
- **多分支同步**：支持按分支列表或通配符 `*` 同步所有分支
- **定时调度**：通过 Cron 表达式灵活配置同步频率，错过执行时间后启动即补跑
- **NuGet 包升级**：可配置自动升级项目 NuGet 包，编译失败自动回滚
- **项目文件更新**：
  - 自动更新 `.csproj` 中的版权年份
  - 以 `NewLife.Core` 为模板同步 `Readme.md` 中"项目矩阵"与"团队信息"段落
  - 以 `NewLife.Core` 为模板同步 GitHub Actions `publish.yml` 中 `.NET SDK` 配置段
  - 以 `NewLife.Core` 为模板同步 `.github/copilot-instructions.md`
- **星尘平台集成**：通过 [Stardust](https://github.com/NewLifeX/Stardust) 接入事件上报与远程命令

---

## 运行环境

| 依赖 | 要求 |
|------|------|
| .NET 运行时 | .NET 8 或更高版本 |
| Git | 已安装并在系统 PATH 中可访问 |
| dotnet-outdated-tool | 仅在启用 NuGet 升级功能时需要（首次运行自动安装） |

---

## 快速开始

### 1. 下载 / 编译

```bash
git clone https://github.com/NewLifeX/GitSync
cd GitSync
dotnet publish -c Release -r win-x64 --self-contained
```

发布产物位于 `Bin/` 目录。

### 2. 首次运行

直接运行可执行文件，程序会在同目录生成默认配置文件 `Sync.config`：

```bash
GitSync.exe
```

日志输出示例：
```
2025-01-01 08:00:00.000  创建定时器：0 0 * * * ?
2025-01-01 08:00:00.001  下次执行时间：2025-01-01 09:00:00
```

---

## 配置文件说明

配置文件为 `Sync.config`（XML 格式），首次运行自动生成，路径与可执行文件同目录。

```xml
<?xml version="1.0" encoding="utf-8"?>
<Sync>
  <!-- 仓库基础目录，Repo.Path 为空时自动拼接 BaseDirectory + Repo.Name -->
  <BaseDirectory>D:\Code</BaseDirectory>

  <!-- Cron 表达式，控制同步频率；多个用分号分隔 -->
  <!-- 默认 "0 0 * * * ?" 表示每小时整点执行一次 -->
  <Crons>0 0 * * * ?</Crons>

  <!-- NuGet 升级时排除的包名，逗号分隔，支持 * 通配符，不区分大小写 -->
  <Excludes>NewLife.*,Stardust.*</Excludes>

  <!-- 仓库列表 -->
  <Repos>
    <Repo Name="MyRepo"
          Enable="true"
          Path=""
          Branchs="master,dev"
          Remotes="origin,github"
          UpdateMode="1" />
  </Repos>
</Sync>
```

### Repo 属性说明

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | string | 仓库名称，同时用于拼接默认本地路径（`BaseDirectory/Name`） |
| `Enable` | bool | 是否参与同步，`false` 时跳过 |
| `Path` | string | 本地路径（绝对路径）；留空则由 `BaseDirectory + Name` 决定 |
| `Branchs` | string | 要同步的分支，逗号分隔；`*` 同步所有本地分支；留空同步当前分支 |
| `Remotes` | string | 要同步的远程库，逗号分隔；`*` 或留空同步所有已配置远程 |
| `UpdateMode` | int | NuGet 升级模式，见下表 |

### NuGet 升级模式（UpdateMode）

| 值 | 说明 |
|----|------|
| `0` (None) | 不升级 NuGet 包（默认） |
| `1` (Exclude) | 升级所有包，但排除 `Excludes` 中配置的包；不含预览版 |
| `2` (Default) | 升级所有包，不排除任何包；不含预览版 |
| `4` (Full) | 全面升级，包括预览版 |

### Cron 表达式格式

格式为 `秒 分 时 日 月 周`，使用 [NewLife Cron](https://github.com/NewLifeX/X) 解析。

| 示例 | 说明 |
|------|------|
| `0 0 * * * ?` | 每小时整点 |
| `0 0 2 * * ?` | 每天凌晨 2 点 |
| `0 30 8,20 * * ?` | 每天 8:30 和 20:30 |
| `0 0/30 * * * ?` | 每 30 分钟 |

---

## 命令行用法

### 默认运行（后台服务模式）

```bash
GitSync.exe
```

程序以后台服务方式运行，按 Cron 定时同步。

### AddAll —— 批量扫描并注册仓库

扫描指定目录下所有包含 `.git` 子目录的仓库，自动追加到 `Sync.config` 并保存。

```bash
GitSync.exe AddAll D:\Code
```

执行后 `Sync.config` 中会自动新增找到的所有仓库（已存在的不重复添加）。

---

## 星尘平台集成（可选）

GitSync 内置 [Stardust](https://github.com/NewLifeX/Stardust) 客户端，支持：

- **事件上报**：同步过程中的关键日志通过 `WriteInfoEvent` / `WriteErrorEvent` 上报到星尘平台
- **远程命令**：在星尘"应用在线"页面可向该应用发送命令

| 命令 | 参数 | 说明 |
|------|------|------|
| `test` | 无 | 立即触发一次全量同步（相当于手动执行一次定时任务） |
| `test` | 仓库名（`Name`） | 立即同步指定仓库 |

配置星尘服务器地址：在 `appsettings.json` 或环境变量中设置 `StarServer` 指向星尘服务器。

---

## 典型使用场景

### 场景 1：镜像推送到多个远程

将同一仓库同步推送到 `gitee`、`github`、`origin` 三个远程：

```xml
<Repo Name="MyProject"
      Enable="true"
      Branchs="master"
      Remotes="origin,github,gitee"
      UpdateMode="0" />
```

### 场景 2：自动升级 NuGet 包并提交

每天凌晨 3 点升级依赖包，编译通过则提交，失败则回滚：

```xml
<Crons>0 0 3 * * ?</Crons>
<Excludes>NewLife.*</Excludes>

<Repo Name="MyProject"
      Enable="true"
      Branchs="dev"
      Remotes="origin"
      UpdateMode="1" />
```

### 场景 3：批量管理目录下所有仓库

```bash
# 一键扫描并注册 D:\Code 下所有 Git 仓库
GitSync.exe AddAll D:\Code

# 然后正常启动，定时同步所有仓库
GitSync.exe
```

---

## 注意事项

- 同步前会检测 `.git/index.lock` 锁定文件，若存在则自动删除，防止因上次异常中断导致阻塞
- 若本地存在**未提交的变更**，对应仓库会跳过此次同步，日志中有提示
- NuGet 升级依赖 [dotnet-outdated-tool](https://github.com/dotnet-outdated/dotnet-outdated)，首次使用时程序会自动安装（需联网）
- `SystemSleep` 防睡眠功能仅在 Windows 平台有效，其他平台无副作用
- Git 操作超时默认 60 秒，可通过修改源码中 `ShellExecute` 的 `msTimeout` 参数调整

---

## 新生命项目矩阵

各项目默认支持最新两个 LTS 版本（.NET 6 / .NET 8），旧版见[历史版本](https://github.com/NewLifeX/NewLife.Core/wiki)。

| 项目 | 描述 |
|------|------|
| [NewLife.Core](https://github.com/NewLifeX/X) | 核心库，提供扩展方法、日志、配置、缓存等基础功能 |
| [NewLife.Stardust](https://github.com/NewLifeX/Stardust) | 星尘分布式平台，提供服务注册发现、配置管理、APM |
| [GitSync](https://github.com/NewLifeX/GitSync) | Git 多仓库同步工具（本项目） |

---

## 新生命开发团队

![智能大石头](https://newlifex.com/stone.jpg)

`新生命团队` 成立于 2002 年，16 年专注于最易用的开源工具。  
团队现有成员 10 人，负责维护多个开源项目，累计 GitHub 数千个 Star。  
欢迎加入 QQ 群 `1600800`（满），或 QQ 群 `1600838`。

> 出品：[新生命开发团队](https://newlifex.com) | 开源地址：[GitHub](https://github.com/NewLifeX/GitSync) / [Gitee](https://gitee.com/NewLifeX/GitSync)
