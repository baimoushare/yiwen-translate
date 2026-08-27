# 自建服务器部署（宝塔面板）

> 适用于：软件下载页 + 诊断信息接收 + 软件内自动更新源，全部挂在自己的阿里云服务器宝塔站点上。
> 部署好之后，国内用户不用翻 GitHub，下载与更新都走自己的服务器。
>
> **当前状态（2026-08-27）**：代码已接入本方案 ——
> - `UpdateService.cs` 更新源指向 `https://update.baimoushare.cn/yiwen/`（SimpleWebSource）
> - `DiagnosticUploadService.cs` 诊断端点指向 `https://update.baimoushare.cn/yiwen/diag/`
> - 下载页模板在 `tools/self-hosted/download.html`，诊断脚本在 `tools/self-hosted/diag-receiver.php`

## 站点结构（宝塔）

宝塔站点 `update.baimoushare.cn`，根目录 `C:/wwwroot/update.baimoushare.cn/YiWen/`：

```
YiWen/                          # 站点根目录
├── index.html                  # 下载页（已上传，用 tools/self-hosted/download.html）
├── diag/
│   ├── index.php               # 诊断接收脚本（用 tools/self-hosted/diag-receiver.php）
│   └── uploads/                # 诊断 zip 存放处（自动创建，755）
└── update/                     # 更新源目录（软件内更新从这里读）
    ├── releases.win.json       # Velopack 更新清单（必须）
    ├── Yiwen-<版本>-full.nupkg # 完整更新包（必须）
    ├── Yiwen-<版本>-delta.nupkg# 增量包（可选，省流量）
    ├── Yiwen-win-Setup.exe     # 安装版（供下载页链接）
    └── Yiwen-win-Portable.zip  # 便携版（供下载页链接）
```

> 更新源目录名 `update/` 对应 URL `https://update.baimoushare.cn/yiwen/update/`。
> 如果你把更新文件直接放 `YiWen/` 根目录，则 URL 是 `https://update.baimoushare.cn/yiwen/` —— 当前代码按「文件放 update/ 子目录」写的，二选一保持一致即可。

## 一、下载页

`tools/self-hosted/download.html` 已上传为 `index.html`。注意两点：
- `href` 里的文件名（`Yiwen-win-Setup.exe` / `Yiwen-win-Portable.zip`）与 update/ 目录里的实际文件一致
- 如需国内直连，下载按钮可直接指向 update/ 目录里的文件

## 二、诊断信息接收

1. 把 `tools/self-hosted/diag-receiver.php` 上传为 `diag/index.php`。
2. 宝塔 → 网站 → PHP 版本 ≥ 7.0。
3. `diag/uploads/` 目录需可写（755）。
4. 验证（客户端是**原始 body 传 zip**，不是表单）：
   ```bash
   echo "PK test zip" > t.zip  # 随便一个 zip
   curl -X POST --data-binary @t.zip -H "Content-Type: application/zip" \
        https://update.baimoushare.cn/yiwen/diag/
   # 返回 {"code":"ABC-123"}（两段 3 位，与客户端 CodePattern 一致）
   ```

## 三、自动更新源（Velopack SimpleWebSource）

Velopack 更新源用 `SimpleWebSource`（静态 HTTPS 目录），它请求 `{baseUri}/releases.win.json`，
**不是** `RELEASES` 文件（RELEASES 是旧 Squirrel 格式，Velopack CLI/GitHub 源用 `releases.<channel>.json`）。

已用 `vpk download http --url http://127.0.0.1:8099/` 实测验证：
```
GET /releases.win.json?arch=x64&os=win&rid=win-x64   ← 读这个
GET /Yiwen-0.0.2-full.nupkg                          ← 然后下载包
校验通过
```

### 代码接入（已完成）

`UpdateService.cs`：
```csharp
private const string GitHubRepoUrl = "https://update.baimoushare.cn/yiwen/";
// CreateManager 里按 URL 判断：http(s) 且非 github.com → SimpleWebSource
return new UpdateManager(new SimpleWebSource(repoUrl), options);
```

### 每次发布后同步到服务器

GitHub 发布后，把 `artifacts/releases/` 里这些上传到 `update/` 目录（宝塔文件管理器或 FTP）：
- `releases.win.json`（必须）
- `Yiwen-<版本>-full.nupkg`（必须）
- `Yiwen-<版本>-delta.nupkg`（可选）
- `Yiwen-win-Setup.exe` / `Yiwen-win-Portable.zip`（给下载页）

> 简易同步：宝塔计划任务加一条 Shell 脚本，用 `gh release download` 拉最新产物到 update/。

## 四、诊断端点（已完成）

`DiagnosticUploadService.cs`：
```csharp
private const string DefaultEndpoint = "https://update.baimoushare.cn/yiwen/diag/";
```
客户端期望响应 `{"code":"XXX-XXX"}`，与 `diag-receiver.php` 一致。

## 五、HTTPS

Velopack 的 SimpleWebSource 与诊断上传都要求 HTTPS。宝塔 → 网站 → SSL → Let's Encrypt 免费证书。
