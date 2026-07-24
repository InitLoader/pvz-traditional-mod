# 外部音频目录

用户自己的短音效放在 `samples/` 子目录，并在 `../config/audio/samples.jsonc` 注册。

仓库只保留本说明，不提交游戏原版音频或用户素材。当前支持由原版 `DSoundManager` 解码的 OGG、WAV 和 AU；背景音乐不放在这里，后续会使用独立的 `music` 运行时和目录。
