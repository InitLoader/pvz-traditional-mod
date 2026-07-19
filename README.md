# PvZ Traditional Mod

面向《植物大战僵尸》PC 版 `1.0.0.1051` 的传统 DLL/EXE Hook 改版工程。当前支持外部关卡出怪、僵尸权重与波次数量倍率、阳光价值、植物攻击、僵尸属性与防具、自定义植物逻辑卡片和选卡分页。

> 本项目是非官方爱好者 Mod，与 PopCap、Electronic Arts 无隶属或授权关系。仓库不提供游戏本体、原版素材、破解工具或 DRM 绕过。使用者必须自行持有合法游戏副本。

## 仓库内容

- `modding/src`：按功能拆分的运行时 Hook 模块。
- `modding/patcher`：仅支持已校验 `1.0.0.1051` 文件的补丁加载器。
- `modding/tests`：配置与生成逻辑回归测试。
- `pvzmod/config`：按关卡、植物、僵尸、UI 和全局设置分类的配置示例。
- `PVZ传统改版技术路线.md`：逆向结论、模块边界、配置规则与后续路线。
- `modding/ARCHITECTURE.md`：代码架构和 Hook 接入说明。

完整配置说明见 [`modding/README.md`](modding/README.md) 与 [`pvzmod/config/README.md`](pvzmod/config/README.md)。

## 构建

需要 Windows、Visual Studio 2022 C++ 工具链与 CMake 3.24 或更新版本：

```powershell
cmake -S modding -B build-win32 -A Win32
cmake --build build-win32 --config Release
ctest --test-dir build-win32 -C Release --output-on-failure
```

构建会下载固定版本的 nlohmann/json 与 MinHook。生成的二进制不会提交到仓库，应通过经过审核的 GitHub Release 分发。

## 安装概要

1. 准备合法的《植物大战僵尸》PC `1.0.0.1051` 游戏目录。
2. 构建 `pvzmod.dll` 与 `PvZModPatcher.exe`。
3. 将 DLL 和 `pvzmod/config` 复制到游戏目录。
4. 使用补丁器处理精确匹配的 EXE；不匹配的哈希或补丁点会被拒绝。
5. 日志写入 `pvzmod/logs/pvzmod.log`。

具体命令、备份行为和配置热重载规则请阅读 [`modding/README.md`](modding/README.md)。

## 开发流程

- `main` 只接收 Pull Request。
- 每次功能或修复使用独立的 `agent/<主题>` 或 `feature/<主题>` 分支。
- PR 必须同步代码、配置示例、技术文档和 `CHANGELOG.md`。
- PR 必须说明影响范围、逆向依据、崩溃风险以及实际验证步骤。
- 禁止提交游戏 EXE、DAT、DLL、图片、动画、音频、存档、日志或反编译生成物。

详细规则见 [`CONTRIBUTING.md`](CONTRIBUTING.md)。
