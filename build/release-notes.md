
## 下载哪个？

| 文件 | 说明 |
|---|---|
| `AreYouThere-{VERSION}-win-x64.msi` | **推荐**：安装版（64 位），带开始菜单快捷方式，可在“应用”中卸载 |
| `AreYouThere-{VERSION}-win-x86.msi` | 安装版（32 位系统） |
| `AreYouThere-{VERSION}-win-x64-portable.exe` | 便携版：单个 exe，下载即用，不需要安装 |
| `AreYouThere-{VERSION}-win-x86-portable.exe` | 便携版（32 位系统） |
| `AreYouThere-{VERSION}-win-arm64.msi` | 安装版（ARM 设备，如 Surface Pro X / Snapdragon 笔记本） |
| `AreYouThere-{VERSION}-win-arm64-portable.exe` | 便携版（ARM 设备） |
| `AreYouThere-{VERSION}-win-x64.zip` / `win-x86.zip` / `win-arm64.zip` | 文件夹版：解压后运行 `AreYouThere.exe` |
| `AreYouThere-{VERSION}.msixbundle` | 未签名的 Microsoft Store 提交包，不能直接双击安装 |
| `AreYouThere-{VERSION}-sideload.msixbundle` | （配置签名证书后）普通用户可侧载安装的签名包 |
| `AreYouThere-sideload.cer` | （配置签名证书后）侧载前导入的公开证书；不包含私钥 |

<!-- unsigned -->
> ⚠️ exe / msi 未签名，Windows 可能提示“未知发布者”，点“更多信息 → 仍要运行”即可。
<!-- /unsigned -->

<!-- signed -->
## MSIX 侧载安装

下载 `AreYouThere-{VERSION}-sideload.msixbundle` 和 `AreYouThere-sideload.cer`。首次安装时，打开 `.cer` → 安装证书 → 本地计算机 → 将所有的证书都放入下列存储 → 受信任的人（需要管理员权限），再双击侧载包安装。

此版本使用自签名证书，Windows 不会自动信任它。只有在确认下载来自本仓库后才导入证书。exe / msi 也可能显示发布者信任提示；签名不代表 Windows 已自动信任发布者。
<!-- /signed -->

「在吗」完全离线运行，不联网、不上传任何数据。需要 Windows 10 1809 或更高版本。
