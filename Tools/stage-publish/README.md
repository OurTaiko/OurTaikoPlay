# 舞台背景的发布

游戏只自带四套演奏背景（`Assets/OurTaiko/Resources/Stage`：`common_0`，以及段位、练习、教程用的三套）。其余的套装不在这个仓库里，发布在公开分发根 `https://distrib.ourtaiko.org/stage/`（S3 桶 `ourtaiko-public`，`ap-northeast-1`；CloudFront 分配 `E29SNWDH0GYCHC`），游戏在进演奏前按需下载。

## 分发根上的布局

```
stage/catalog.json                                    bundle 版本 1 的目录表，已发布的旧版游戏在读，不再更新
stage/bundle/<版本>/catalog.json                       该版本的目录表：所有套装和 preset（来源是本目录的 catalog.json）
stage/otanim/index.json                               原始文件的清单（名字、文件、大小、SHA-256）
stage/otanim/<部件>/<名字>.<hash16>.otanim              原始文件，网站预览和 CI 打包用
stage/bundle/<版本>/<平台>/index.json                  该平台 bundle 的清单
stage/bundle/<版本>/<平台>/<部件>-<名字>.<hash16>        一个文件一个 Unity AssetBundle
```

- `<hash16>` 是文件 SHA-256 的前 16 位。除了 JSON，已发布的文件从不原地替换，也不删除：素材变了就是新文件名，只有 JSON 指向当前的那个，已发布的游戏继续读旧文件。
- 平台是 `windows`、`macos`、`linux`、`android`、`ios`。
- 上传顺序保证 JSON 不会指向还不存在的东西：先文件后清单；目录表要等五个平台的 bundle 都传完才上传。
- 试验时可以用别的目录名（如 `stage-test`），布局相同。

## 玩家自己加背景

游戏目录下的 `Stage/` 文件夹可以放散装的 `.otanim`，布局与上面的 `otanim/` 相同（`Stage/<部件>/<名字>.otanim`，不带 hash），同名时优先于下载的和自带的；再放一个可选的 `catalog.json`，它的套装和 preset 加到游戏的目录表上。细节见 `Assets/OurTaiko/Runtime/Play/StageLibrary.cs` 和 `StageCatalogFile.cs`。

## 加或改一套已发布的背景

1. 在 `Tools/stage-publish/catalog.json` 里登记（套装、是否进随机池、preset）。它允许 `//` 和 `/* */` 注释。
2. 把 `.otanim` 放进 `Assets/OurTaiko/StageSource/<部件>/`（已被 `.gitignore` 忽略，不进仓库）。预览：把文件放进游戏目录的 `Stage/<部件>/`，进演奏看效果。
3. 排出原始文件并上传（需要 `aws login`，有桶和分配的写权限）：

   ```bash
   Tools/stage-publish/build.py <输出目录>
   Tools/stage-publish/publish.sh <输出目录>            # 第二个参数可换成 stage-test 等试验目录
   ```

   `build.py` 只写 `stage/otanim/`，目录表列出的套装缺文件时什么都不写。`publish.sh` 只上传 `otanim/`：先文件、后 `index.json`，再让 CDN 上的 `index.json` 失效；不删除任何东西。
4. 提交 `catalog.json` 的改动并合并。
5. 在 GitHub 上手动运行 **Stage bundles** 工作流（`folder` 默认 `stage`）：它从分发根取原始文件，五个平台各打一份 bundle 上传，全部成功后把这个提交里的 `catalog.json` 上传为 `bundle/<版本>/catalog.json`（版本取 `StageLibrary.BundleVersion`），并让 CDN 上的 JSON 失效。

## 工作流需要的 AWS 角色

仓库变量 `STAGE_PUBLISH_ROLE_ARN` 是工作流经 GitHub OIDC 扮演的角色；区域默认 `ap-northeast-1`，可用变量 `STAGE_PUBLISH_REGION` 覆盖。账号里需要先有 `token.actions.githubusercontent.com` 这个 OIDC 身份提供商（受众 `sts.amazonaws.com`）。

信任策略（只允许本仓库 `main` 上运行的工作流）：

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Principal": { "Federated": "arn:aws:iam::<账号>:oidc-provider/token.actions.githubusercontent.com" },
    "Action": "sts:AssumeRoleWithWebIdentity",
    "Condition": {
      "StringEquals": {
        "token.actions.githubusercontent.com:aud": "sts.amazonaws.com",
        "token.actions.githubusercontent.com:sub": "repo:OurTaiko@252237704/OurTaikoPlay@1400725485:ref:refs/heads/main"
      }
    }
  }]
}
```

所以工作流要从 `main` 运行；从别的分支运行会在登录 AWS 时被拒。

`sub` 里的组织和仓库后面带着 `@数字 ID`：这个仓库开了 GitHub OIDC 的不可变身份（`use_immutable_subject`），工作流报出的身份就是这个形式。写成不带 ID 的 `repo:OurTaiko/OurTaikoPlay:…` 对不上，登录会以 `Not authorized to perform sts:AssumeRoleWithWebIdentity` 失败。当前的前缀可以这样查：

```bash
gh api repos/OurTaiko/OurTaikoPlay/actions/oidc/customization/sub
```

最小的权限策略（只能写 `stage*` 目录、按前缀列出，以及让分配上的缓存失效；不能删除）：

```json
{
  "Version": "2012-10-17",
  "Statement": [
    { "Effect": "Allow", "Action": "s3:PutObject", "Resource": "arn:aws:s3:::ourtaiko-public/stage*" },
    { "Effect": "Allow", "Action": "s3:ListBucket", "Resource": "arn:aws:s3:::ourtaiko-public",
      "Condition": { "StringLike": { "s3:prefix": "stage*" } } },
    { "Effect": "Allow", "Action": "cloudfront:CreateInvalidation",
      "Resource": "arn:aws:cloudfront::<账号>:distribution/E29SNWDH0GYCHC" }
  ]
}
```

若让角色写整个桶，把 `PutObject` 的资源换成 `arn:aws:s3:::ourtaiko-public/*`，去掉 `ListBucket` 的前缀条件即可。删除对象（`s3:DeleteObject`）和改桶设置的权限都不需要，不要给。
