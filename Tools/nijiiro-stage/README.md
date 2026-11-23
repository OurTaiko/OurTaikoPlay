# Nijiiro 舞台素材转换

从本地 YataiDON 的皮肤图像和 Lua 动画表生成游戏使用的 otanim v1。新建空目录作为输出即可独立检查结果。输入使用可信的本地皮肤，转换器会执行其中的 Lua 脚本。

需要 Python 3.9 或更新版本、Pillow 和 Lua 5.3 或更新版本。在 OurTaikoPlay 根目录运行：

```sh
python3 Tools/nijiiro-stage/export.py ~/Repos/YataiDON/Skins/YataiDONNijiiro /tmp/nijiiro-stage
```

输入版本为 YataiDON 的 `a3b8a037`。同一 checkout 的 `PyTaikoGreen` 提供段位、练习和四个单人联动所继承的素材。输出按 `Resources/Stage` 的子目录排列。文件名由 `names.py` 决定，同一套在每个目录里用同一个名字：谁都可能抽到的套装叫 `common_<n>`（皮肤里的 `a_01`、`bg_0`、`bg_fever_0`、`footer0`、`renda_0`、`dancer_0`、观众 `0`），`#SCENEPRESET` 才用的叫联动自己的名字（`b_001` 是 `miku`，皮肤加的 `_n`、`_f`、`_dai`、`_d` 去掉，由目录表示）。游戏自己的 SimpleStyle 和原段位头带继续由原工程制作。Unity 的 GUID、导入压缩设置、随机池权重和预设对应关系由游戏仓库管理。

舞者的 `rig` 每拍 40 帧，各成员有自己的入场、舞蹈循环和离场位置。头带和下方舞台中周期不同的图层可以各自循环。观众的 `full` 包含出场和循环，失去满魂时播放 `release`。普通曲目关闭击中小人，四个继承预设的 `chibi` 文件提供 `good_*` 和 `bad_*`。

播放端会把同一个部件从上一帧的位置渐变到下一帧，所以一张图跳回滚动起点时（循环接头处最常见）要换一个部件，否则会被横扫过整个画面一帧。`bake.py` 的 `Bake.clip` 在一张图一帧内移动超过 32 像素、且是它原来速度的 4 倍以上时换部件（同一张图的另一份，贴图不变）。
