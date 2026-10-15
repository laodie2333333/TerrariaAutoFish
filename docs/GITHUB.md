# 将项目上传到 GitHub

推荐仓库名：`TerrariaAutoFish`。源码放仓库，编译好的便携 ZIP 放 Releases。

本机已准备 `dist/TerrariaAutoFish-0.3.0-source.zip` 供网页上传使用。修改源码后可运行 `powershell -ExecutionPolicy Bypass -File .\tools\Package-Source.ps1` 重新生成；该脚本按目录白名单收集文件，包含 `.github`，排除开发缓存和游戏检查输出。

## 方法一：GitHub 网页上传

1. 登录 https://github.com ，点击右上角 **+ → New repository**。
2. 仓库名填写 `TerrariaAutoFish`，选择 Public 或 Private。先不要勾选自动生成 README、.gitignore 或许可证，本项目已经准备好了这些文件。
3. 创建后点击 **uploading an existing file**；已有内容时使用 **Add file → Upload files**。
4. 解压本项目准备的源码 ZIP，打开解压后的文件夹，上传里面的源码目录和根文件。不要把 ZIP 文件本身作为源码上传，不要再套一层项目文件夹。需要包含 `.github`，这样自动测试和问题模板才能启用。
5. 填写提交说明，例如 `Initial release of TerrariaAutoFish`，点击 **Commit changes**。首次上传 `.github/workflows/tests.yml` 如果网页限制该路径，可先上传其他文件，再通过仓库的 **Add file → Create new file** 按同一路径创建工作流文件。

源码应包含 `src`、`tests`、`tools`、`assets`、`docs`、`.github`、构建/测试脚本、README、LICENSE、第三方声明、更新记录、`.gitignore` 和 `.gitattributes`。

不要上传 `.git`、`.local`、`.tools`、`.venv`、游戏 EXE / DLL / Content、反编译文件、角色或世界存档。网页上传不会自动帮你应用 `.gitignore`，所以应使用准备好的源码包，而不是直接拖整个开发目录。

## 方法二：本机 Git 推送

本项目目录已初始化 Git。先在 GitHub 创建一个空仓库，然后在该目录打开 PowerShell。下面的 `YOUR_NAME` 要替换为你的 GitHub 用户名：

```powershell
git status --short
git add .
git diff --cached --stat
git commit -m "Prepare TerrariaAutoFish 0.3.0"
git branch -M main
git remote add origin https://github.com/YOUR_NAME/TerrariaAutoFish.git
git push -u origin main
```

首次推送按 Git Credential Manager 提示登录；GitHub 不接受账号密码作为 Git HTTPS 密码，不要把访问令牌写进仓库地址、代码或截图。如果 origin 已存在，先用 `git remote -v` 核对地址，再决定是否使用 `git remote set-url origin ...`。

提交前可运行 `git var GIT_AUTHOR_IDENT` 检查作者名与邮箱。如果想保护邮箱，使用 GitHub **Settings → Emails** 显示的 noreply 邮箱，再用 `git config user.email '你的 noreply 邮箱'` 设置此仓库。

以后修改代码，先测试，再 `git add .`、`git commit -m "说明修改"`、`git push` 即可。Git 会应用 `.gitignore`，开发缓存不会进入提交。

## 发布可直接运行的程序

1. 仓库页面右侧 **Releases → Create a new release**。
2. 创建标签 `v0.3.0`，标题可写 `v0.3.0 — 声纳鱼获筛选与界面改进`，填写更新说明。
3. 上传 `dist/TerrariaAutoFish-1.4.5.8.zip`，点击 **Publish release**。

用户在 Release 下载并完整解压 ZIP，然后运行 EXE；GitHub 自动生成的 **Source code** ZIP 只有源码，不能直接作为成品运行。版本号 `0.3.0` 是工具版本，`1.4.5.8` 是适配的 Terraria 版本。

GitHub Actions 仅运行无需游戏的逻辑和日志检查；完整编译和游戏启动验证需要本机正版 Terraria / XNA，不会把游戏文件上传到 CI。初次公开发布建议标记为 **pre-release**，因为多人和长时间后台挂机仍待验证。

原创代码使用 MIT 许可；金钓竿图标等第三方素材的归属和许可范围见 `THIRD_PARTY_NOTICES.md`。
