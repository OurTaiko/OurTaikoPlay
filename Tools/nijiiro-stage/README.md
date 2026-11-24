# Nijiiro 舞台素材转换

从本地 YataiDON 的皮肤图像和 Lua 动画表生成游戏使用的 otanim v1。新建空目录作为输出即可独立检查结果。输入使用可信的本地皮肤，转换器会执行其中的 Lua 脚本。

需要 Python 3.9 或更新版本、Pillow 和 Lua 5.3 或更新版本。在 OurTaikoPlay 根目录运行：

```sh
python3 Tools/nijiiro-stage/export.py ~/Repos/YataiDON/Skins/YataiDONNijiiro /tmp/nijiiro-stage
```

输入版本为 YataiDON 的 `a3b8a037`。同一 checkout 的 `PyTaikoGreen` 提供段位、练习和四个单人联动所继承的素材。输出按 `Resources/Stage` 的子目录排列。文件名由 `names.py` 决定，同一套在每个目录里用同一个名字：谁都可能抽到的套装叫 `common_<n>`（皮肤里的 `a_01`、`bg_0`、`bg_fever_0`、`footer0`、`renda_0`、`dancer_0`、观众 `0`），`#SCENEPRESET` 才用的叫联动自己的名字（`b_001` 是 `miku`，皮肤加的 `_n`、`_f`、`_dai`、`_d` 去掉，由目录表示）。游戏自己的 SimpleStyle 和原段位头带继续由原工程制作。Unity 的 GUID、导入压缩设置、随机池权重和预设对应关系由游戏仓库管理。

连打特效（`renda`）画在整个 1920×1080 的演奏画面上（`StageRendaView`），文件也是这个大小，同时出现几个不设上限。出生位置和动作照 PyTaikoGreen 移植时记下的街机数值（720p，乘 1.5 换到 1080）：Nijiiro 自己的 `RendaClip` 和覆盖版 `renda.lua` 的出生范围是凭眼估的（左右边缘、y 540–1020），拿街机视频核对是错的（IDOLiSH7 的心全是从屏幕左下底边之下、x 90–924 一带飞出来的）。`backgrounds.py` 的 `RENDA_AREA` 是各联动的出生范围：miku/gumi/ia 从右边缘外横穿，斜飞的几套从左下底边之下，mario 在下半屏任意处，TOHO 在舞者上方。`other.py` 的三套通用连打照 PTG `renda.lua` 的 `SPECS`（各图的飞行时间、位移、旋转、出生范围）。四个继承的单人联动由 `parent.lua` 直接跑 PTG 的脚本，取出生范围的中点。每段都从范围中点出生，目录表的 `spread` 是范围的半宽（脚本会打印）。素材大小按 1080 自适配：TOHO 的数据和图、Nijiiro 的三套通用连打图都是 720p 的（和 PTG 的同一张逐个相等），一律放大 1.5 倍；其他各套已经是 1080 的。

舞者的 `rig` 每拍 40 帧，各成员有自己的入场、舞蹈循环和离场位置。头带和下方舞台中周期不同的图层可以各自循环。观众的 `full` 包含出场和循环，失去满魂时播放 `release`。普通曲目关闭击中小人，四个继承预设的 `chibi` 文件提供 `good_*` 和 `bad_*`。

播放端会把同一个部件从上一帧的位置渐变到下一帧，所以一张图跳回滚动起点时（循环接头处最常见）要换一个部件，否则会被横扫过整个画面一帧。`bake.py` 的 `Bake.clip` 在一张图一帧内移动超过 32 像素、且是它原来速度的 4 倍以上时换部件（同一张图的另一份，贴图不变）。
