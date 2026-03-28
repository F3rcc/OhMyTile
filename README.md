<div align="center">
  <img src="OhMyTile/Assets/Square150x150Logo.png" width="150" height="150" alt="OhMyTile Logo">
  <h1>OhMyTile</h1>
  <p>回来吧！我美丽的磁贴！</p>
</div>

# 效果 #

![totalshow](/OhMyTile/Assets/totalshow.png)

# 注意 #

## 此工具仅在win10系统上进行过测试，不保证win11系统的可行性！ ##

windows11已经删除了磁贴功能，如果需要开启磁贴功能，请自行搜索解决方法，本工具无法保证在win11上使用了一些方法调回磁贴后，仍然可以正常运行

此外，由于windows10系统在20H2版本之后对磁贴的自定义颜色功能进行了调整，因此，如果要实现完美的磁贴，请手动到以下注册表路径中进行修改：

>HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\0\2093230218

将EnabledState的值改为1

bilibili中有视频介绍恢复自定义磁贴功能: BV1PQVwzYEPM

Youtube中提到该操作的视频：

https://www.youtube.com/watch?v=VAiTO36j5nI&list=LL&index=24

## 🌟 简介 (Introduction)

**OhMyTile** 是一款基于 WPF 与 Desktop Bridge (MSIX) 架构开发的 Windows 开始菜单美化工具。

    由于Tile Genie的下架，以及本人电脑死活用不了Tile Genie，故而自己动手编写了此程序（使用了AI）。

它不仅恢复了 Windows 原生支持但未开放界面的**自定义背景色**功能，还突破性地引入了**原画级物理切割**与**底层 XML 动画排期引擎**，让你能轻松将二次元老婆完美无损地固定在开始菜单上，并且支持图片的轮换功能。

## ✨ 核心特性 (Features)

- **纯血单块定制 (Single Tile Customization)**

  ![show1](/OhMyTile/Assets/show1.png)

  - 智能提取 PE 核心内的高清 256px Jumbo 原始图标
  - 特别优化：深度解析 `.url` 格式，**完美支持 Steam 桌面快捷方式图标一秒抓取**
  - 内置 SAI 风格色环与全局屏幕吸色器，支持一键隐藏磁贴名称与黑白字体切换
  - 可以选择图片的缩放比例
  - 可以使用自定义的图片，支持`.ico .png .jpg`等图片格式，但是需要进行裁剪

- **大图切割模式 (Tile Genie Mode)**

  ![show2](/OhMyTile/Assets/show2.png)

  - 彻底告别模糊与割裂！采用 GDI+ 结合物理 DPI 逆向映射算法，切割时自动剥离 `4px` 间隙，拼接到开始菜单后严丝合缝。
  - 支持滚轮实时缩放与拖拽定位，可视化网格配置。
  - 右键任意磁贴无缝切换 **宽磁贴 / 正方形**。

- **动态轮换引擎 (Live Tile Engine)**

  **❗❗该模式下存在较多暂时无法解决的问题，请酌情使用❗❗**

  - **原生 3D 轮换**：一键写入 Windows 底层通知队列 (Notification Queue)，享受极致丝滑的 3D 翻转动画。
      ### 注意： ###
        该模式下，由于windows系统的限制，磁贴会错开时间进行翻转，也就可能导致图片错位，例如固定了四张磁贴，只有三张变成了下一张图片，只剩一张还未变化。起初这并不符合我的预期，但是想到了有一些主体大致相同，但是只有细微部分不一样的图片，如果是这种模式，可能会存在意想不到的效果，如：两张二次元图片，主体人物不变，但是表情却是不同的。故而进行了保存，但是通常情况下本人不推荐使用windows系统的自带轮换机制。

  - 示范：

    ![cycle1](/OhMyTile/Assets/cycle1.gif)

  - **定时同步切换** ：模仿Tile Genie，但是做出了一定优化，使用了定时计划任务方法，也就是为每一个磁贴写入接下来的变化计划，但是由于系统限制，计划任务存在上线，因此需要在磁贴不再会自动变化的时候，自己手动取消，再重新固定一遍

      ### 注意 ###
        该模式下，请尽量减少图片序列（程序上限为5张），并且对于轮换的时间，建议调大一点，请勿使用 15s 这样的间隔。

        windows系统自身存在一些问题，为了省电以及性能，开始菜单会处于一种冻结的状态，该状态下，即使到了图片轮换的时间，windows系统也不会立即去根据计划好的任务进行轮换，而是等到你打开开始菜单的那一刻，才进行更新，所以有的时候会看到磁贴莫名其妙的自己转了一圈，图片也还是原本那样。

        如果开始菜单突然闪了一下，并且固定的磁贴有一些变为空白了，这说明固定的磁贴产生的计划任务太多了，导致windows的开始菜单崩溃，磁贴缓存丢失，但是本程序使用的计划任务仍然会执行，所以下一次需要图片轮换的时候依旧会正常轮换。如果出现这种情况，请适量减少磁贴数量以及增大轮换间隔。


  - 示范：

    ![cycle2](/OhMyTile/Assets/cycle2.gif)

## 🚀 安装指南 (Installation)

由于涉及 Windows 底层的磁贴 UWP API 调用，本程序采用免打包的纯天然 `AppxManifest.xml` 注册机制，需要手动打开开发人员模式。powershell的命令将由`.bat`脚本自动进行。

1. 打开 `Windows 设置` -> `更新与安全` -> `开发者选项`，**开启「从任意源（包括松散文件）安装应用」**。
![setting](/OhMyTile/Assets/setting.png)
2. 下载本仓库 Release 压缩包并解压。
3. 双击运行 **一键安装或更新.bat**