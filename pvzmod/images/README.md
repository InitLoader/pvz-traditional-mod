# 外部图片目录

用户自有或获得合法授权的 Mod 图片放在此目录，并按用途继续分类，例如：

```text
pvzmod/images/
├─ zi/       # 僵尸精英覆盖贴图
├─ plants/   # 植物和卡片贴图
└─ ui/       # UI 图标和界面元素
```

`pvzmod/config/resources/textures.jsonc` 中的路径必须从 `pvzmod/images/` 开始。默认示例注册 `KILL`，对应本地文件：

```text
pvzmod/images/zi/kill.png
```

仓库不提供该图片。图片缺失或解码失败时，使用它的功能会跳过该覆盖层并在日志中记录一次警告，其他精英属性和游戏逻辑继续运行。

本目录默认只跟踪此说明文件，避免误提交游戏原版素材。需要发布自制图片时，应在独立 Pull Request 中确认版权来源后调整 allow-list。
