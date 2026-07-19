# 贡献与 Pull Request 规范

## 分支与提交

1. 从最新 `main` 创建一个主题分支，例如 `feature/elite-zombie`、`fix/seed-page-hitbox` 或 `agent/update-docs`。
2. 一个分支只处理一个可独立验证的主题。
3. 提交信息使用简短祈使句，说明实际结果。
4. 禁止直接向 `main` 推送日常功能修改；通过 Pull Request 合并。

## 每个 PR 必须包含

- 修改目的和使用者可见影响。
- 受影响的游戏函数、对象偏移或配置 Schema。
- 原版行为、覆盖规则和不配置时的回退行为。
- 构建结果、自动测试和实际进游戏验证结果。
- 崩溃风险、兼容版本和恢复方式。
- 同步更新配置注释、`PVZ传统改版技术路线.md`、`modding/ARCHITECTURE.md` 与 `CHANGELOG.md` 中相关部分。

## 安全边界

- 只支持项目明确校验过的游戏版本与补丁点；字节不匹配时必须拒绝 Hook 或补丁。
- 禁止提交游戏本体、原版资源、DRM 文件、存档、日志、崩溃转储和反编译生成物。
- 不得用一个巨型 Hook 类集中全部业务。每种配置和功能保持独立模块，公共层只放版本验证、Hook 安装和稳定的数据结构。
- 配置保持稀疏覆盖：没有写出的关卡或属性继续使用原版值。

## 验证基线

```powershell
cmake -S modding -B build-win32 -A Win32
cmake --build build-win32 --config Release
ctest --test-dir build-win32 -C Release --output-on-failure
```

涉及 UI、动画、对象布局或裸汇编跳板时，自动测试不能代替实际进游戏验证。PR 中应记录进入过的关卡、交互路径和观察结果。
