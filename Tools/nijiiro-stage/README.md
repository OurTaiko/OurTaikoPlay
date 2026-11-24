# Nijiiro 舞台素材转换

从本地 YataiDON 的皮肤图像和 Lua 动画表生成游戏使用的 otanim v1。新建空目录作为输出即可独立检查结果。输入使用可信的本地皮肤，转换器会执行其中的 Lua 脚本。

需要 Python 3.9 或更新版本、Pillow 和 Lua 5.3 或更新版本。在 OurTaikoPlay 根目录运行：

```sh
python3 Tools/nijiiro-stage/export.py ~/Repos/YataiDON/Skins/YataiDONNijiiro /tmp/nijiiro-stage
```

输入版本为 YataiDON 的 `a3b8a037`。同一 checkout 的 `PyTaikoGreen` 提供段位、练习和四个单人联动所继承的素材。输出按 `Resources/Stage` 的子目录排列。文件名由 `names.py` 决定，同一套在每个目录里用同一个名字：谁都可能抽到的套装叫 `common_<n>`（皮肤里的 `a_01`、`bg_0`、`bg_fever_0`、`footer0`、`renda_0`、`dancer_0`、观众 `0`），`#SCENEPRESET` 才用的叫联动自己的名字（`b_001` 是 `miku`，皮肤加的 `_n`、`_f`、`_dai`、`_d` 去掉，由目录表示）。游戏自己的 SimpleStyle 和原段位头带继续由原工程制作。Unity 的 GUID、导入压缩设置、随机池权重和预设对应关系由游戏仓库管理。

连打特效（`renda`）画在整个 1920×1080 的演奏画面上（`StageRendaView`），文件也是这个大小，效果照 YataiDON：每段从 y 780 出生；联动（`backgrounds.py`）横向按 YataiDON `RendaClip` 的规则取 `dx`：往左飞的从右边缘、往右飞的从左边缘、不动的在中间（`renda_spawn`）；三套通用连打（`other.py`）在皮肤出生范围 x 6–1100 的中点；四个继承的单人联动按 PyTaikoGreen 的出生范围取中点。游戏再按目录表的 `spread`（各出生范围的半宽）随机偏移，同时出现几个不设上限。素材大小按 1080 自适配：TOHO 的数据和图是街机 1280×720 的（和 PyTaikoGreen 的 720p 手写版逐个相等），其他各套都已是 1080 的，所以 `RENDA_720P` 让它绕出生点整体放大 1.5 倍；它的 `dx` 也不表示方向（扇形向两边散），所以不用上面的规则，从 PyTaikoGreen 记下的街机出生范围中点 (960, 465)（舞者上方）出生，`spread` 是 `[360, 15]`。

舞者的 `rig` 每拍 40 帧，各成员有自己的入场、舞蹈循环和离场位置。头带和下方舞台中周期不同的图层可以各自循环。观众的 `full` 包含出场和循环，失去满魂时播放 `release`。普通曲目关闭击中小人，四个继承预设的 `chibi` 文件提供 `good_*` 和 `bad_*`。

播放端会把同一个部件从上一帧的位置渐变到下一帧，所以一张图跳回滚动起点时（循环接头处最常见）要换一个部件，否则会被横扫过整个画面一帧。`bake.py` 的 `Bake.clip` 在一张图一帧内移动超过 32 像素、且是它原来速度的 4 倍以上时换部件（同一张图的另一份，贴图不变）。
