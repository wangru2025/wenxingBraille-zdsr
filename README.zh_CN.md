# 文星点显器争渡插件

这是用于争渡读屏的文星 / CBP 40 方点显器插件。

本项目直接通过 WinUSB 实现文星设备协议，不包含阳光读屏文件、`StarLibDriver.dll` 或任何专有驱动二进制文件。

## 声明

本项目是非官方兼容插件，不隶属于文星 / CBP 设备厂商、阳光读屏、争渡读屏或 NV Access，也未得到上述主体的官方认可或支持。

安装包只安装本插件，不包含或分发阳光读屏文件、`StarLibDriver.dll`、争渡读屏二进制文件、NVDA 二进制文件或其他专有组件。用户应通过合法渠道取得争渡读屏、点显器和所需驱动，并遵守其各自的许可协议。

## 要求

- 已安装争渡读屏，并带有 `ZDSRBrailleDisplayAddin.dll`。
- .NET Framework 4.x 运行时。
- 构建安装包需要 Inno Setup 6。
- 使用 WinUSB 接口的文星 / CBP 兼容点显器。

## 构建

运行：

```powershell
.\build.ps1
```

插件 DLL 会生成到 `dist\app\wenxingBraille.dll`。

构建安装包：

```powershell
.\build-installer.ps1
```

安装包会生成到 `dist\wenxingBraille-zdsr-1.0.2-Setup.exe`。

## 安装

安装程序会把插件复制到：

```text
{app}\addins\BrailleDisplay\wenxingBraille
```

默认 `{app}` 是 `C:\Program Files (x86)\zdsr\zdsr`。

## 许可证

MIT
