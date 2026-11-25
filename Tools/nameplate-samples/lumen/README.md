# 名牌动画制作示例

在 Unity 的 **OurTaiko → Nameplate Preview** 里点击 **Install Lumen examples**。选用 Band 的 `lumen-ai-3` 和 Icon 的 `lumen-rankup`。名牌需要显示称号或段位。安装会保留同名的本地文件。

- 动态称号的 `idle` 按 60 fps 播放 300 帧。打开 Pause 后用 +1 frame 看扫光经过局部矩形遮罩的边缘。转换器在名牌坐标系中裁剪网格，并在新顶点插值 UV。其他装饰按原有深度绘制。
- Icon 默认的 `idle` 为空。在 **Icon clip** 选择例如 `1dan/rankup_start`，播放后会停在等待画面。可以单选 `1dan/rankup_wait` 查看该帧，再选 `1dan/rankup_end` 观看淡出。每个段位各有三个片段。
- 点击 **Wear as 1P** 后，段位结算进入升段庆祝时按课程段位选择 start，离开庆祝时选择 end。切场继续沿用游戏原本的时间。片段名遵循源素材标签，其他图标也可以提供相同片段。

## 从解包重建

需要 Python 3、Pillow、Go，以及与 OurTaikoPlay 同级的 otanim 仓库。从 OurTaikoPlay 根目录执行以下命令，第二个路径是要写入的示例目录。

```sh
python3 Tools/nameplate-samples/lumen/bake.py ~/Downloads/name_plate_decoded Tools/nameplate-samples/lumen/items
(cd Tools/nameplate-samples && go test ./lumen)
```

转换器只读取 `layout.json` 和 `atlas.png`。先从主精灵定位 `title_instance` 的 `ai_3` 和 `rankup_instance`。每帧按深度更新显示列表，合成父子变换，将源坐标原点映射到 408 × 96 画布中心。升段的 19 套段位字共用一个图集。最后使用 otanim 的 Go 参考编码器写出 v1 文件，再解码检查。两个示例覆盖的遮罩是完全不透明的轴对齐矩形；其他形状的遮罩和颜色加值需要单独实现。

升段源时间轴的第 5 至 46 帧构成开始片段（从 0 计数），第 46 帧为停止帧，第 47 至 70 帧构成结束片段。开始片段包含等待帧，使游戏在等待操作期间自然保持最后一帧。
