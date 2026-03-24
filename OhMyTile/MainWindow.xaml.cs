using System;
using System.IO;
using System.Drawing; // GDI+ 核心
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.UI.StartScreen;
using Windows.Storage;
using System.Text;
using System.Threading.Tasks;

namespace OhMyTile;

public partial class MainWindow : Window
{
    private bool _isUpdatingScale = false;
    private string? _customCroppedPath; 
    private string? _extractedIconPath; 

    // --- Win32 API 声明 (TileBeautify 核心方案) ---
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    

    public MainWindow()
    {
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        InitializeComponent();

        BgColorInput.TextChanged += (s, e) => {
            try {
                // 显式指定 System.Windows.Media 以解决颜色转换器冲突
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(BgColorInput.Text);
                TilePreview.Background = new System.Windows.Media.SolidColorBrush(color);
            } catch { }
        };
        
        this.Loaded += (s, e) => UpdatePreviewScale(IconScaleSlider.Value);
    }

    private void UpdateDefaultIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try {
            string realPath = GetShortcutTarget(path);
            IntPtr[] phicon = new IntPtr[1];
            uint[] piconid = new uint[1];

            // 1. 强制提取 256x256 原生无损资源
            PrivateExtractIcons(realPath, 0, 256, 256, phicon, piconid, 1, 0);

            if (phicon[0] != IntPtr.Zero) {
                // 2. 【核心修复】显式使用 System.Drawing.Icon 解决 CS1061 错误
                using (System.Drawing.Icon icon = System.Drawing.Icon.FromHandle(phicon[0]))
                using (System.Drawing.Bitmap rawBmp = icon.ToBitmap()) {
                    // 更新预览 (WPF 转换)
                    IntPtr hBitmap = rawBmp.GetHbitmap();
                    ImagePreview.Source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    DeleteObject(hBitmap);

                    // 3. 缓存 256px 原始文件作为合成源
                    string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OhMyTile");
                    if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                    _extractedIconPath = Path.Combine(cacheDir, "master_source.png");
                    
                    rawBmp.SetResolution(96f, 96f);
                    rawBmp.Save(_extractedIconPath, ImageFormat.Png);
                }
                DestroyIcon(phicon[0]);
                UpdatePreviewScale(IconScaleSlider.Value);
            }
        } catch { }
    }

    // --- 极致清晰渲染器：复刻 TileBeautify 参数 ---
    private void GenerateTileImage(string sourcePath, string targetPath, double scalePercent)
    {
        const int canvasSize = 310;
        System.Windows.Media.Imaging.BitmapImage source = LoadImageNoLock(sourcePath);

        // 强制 Pbgra32
        var pbgraSource = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Pbgra32, null, 0);

        System.Windows.Media.Imaging.RenderTargetBitmap rtb = 
            new System.Windows.Media.Imaging.RenderTargetBitmap(canvasSize, canvasSize, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        
        System.Windows.Media.DrawingVisual visual = new System.Windows.Media.DrawingVisual();
        using (System.Windows.Media.DrawingContext dc = visual.RenderOpen())
        {
            // 【核心修复 3】使用透明黑底板。这样缩放算法即使溢出，吸到的也是黑色透明，不会产生白边
            var transparentBlack = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0));
            dc.DrawRectangle(transparentBlack, null, new System.Windows.Rect(0, 0, canvasSize, canvasSize));

            double iconSize = canvasSize * (scalePercent / 100.0);
            double offset = (canvasSize - iconSize) / 2.0;

            System.Windows.Media.RenderOptions.SetBitmapScalingMode(visual, System.Windows.Media.BitmapScalingMode.Fant);
            System.Windows.Media.RenderOptions.SetEdgeMode(visual, System.Windows.Media.EdgeMode.Aliased);

            // 【核心修复 4】如果是自定义图片（通常 scalePercent 会改变），执行 1 像素物理避让
            var clipRect = new System.Windows.Media.RectangleGeometry(
                new System.Windows.Rect(offset + 1, offset + 1, iconSize - 2, iconSize - 2));
            dc.PushClip(clipRect);

            dc.DrawImage(pbgraSource, new System.Windows.Rect(offset, offset, iconSize, iconSize));
            
            dc.Pop();
        }

        rtb.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using (var fs = new System.IO.FileStream(targetPath, System.IO.FileMode.Create)) { encoder.Save(fs); }
    }

    private async void CreateCustomLauncherTile_Click(object sender, RoutedEventArgs e)
    {
        try {
            string? imageToUse = _customCroppedPath ?? _extractedIconPath;
            if (string.IsNullOrEmpty(imageToUse) || string.IsNullOrEmpty(TargetPathInput.Text)) {
                MessageBox.Show("请确认目标和图标！"); return;
            }

            string uniqueId = "Tile" + DateTime.Now.Ticks.ToString().Substring(10);
            string encodedTarget = Convert.ToBase64String(Encoding.UTF8.GetBytes(TargetPathInput.Text));
            string launchArgs = $"omt_action=open&target={encodedTarget}";

            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            string fileName = uniqueId + ".png";
            string fullPath = Path.Combine(localFolder.Path, fileName);

            // 执行 GDI+ 极致渲染
            GenerateTileImage(imageToUse, fullPath, IconScaleSlider.Value);

            await Task.Delay(150); // 确保 IO 释放

            SecondaryTile tile = new SecondaryTile(uniqueId, TileNameInput.Text, launchArgs, new Uri("ms-appdata:///local/" + fileName), TileSize.Square150x150);
            tile.VisualElements.ShowNameOnSquare150x150Logo = true;
            
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(BgColorInput.Text);
            tile.VisualElements.BackgroundColor = Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B);
            tile.VisualElements.ForegroundText = ForegroundText.Light;

            WinRT.Interop.InitializeWithWindow.Initialize(tile, new WindowInteropHelper(this).Handle);
            await tile.RequestCreateAsync();
        } catch (Exception ex) { MessageBox.Show("固定失败: " + ex.Message); }
    }

    // --- 其他辅助逻辑（快捷方式/图片加载/UI同步） ---
    private string GetShortcutTarget(string path)
    {
        try {
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
            Type shellType = Type.GetTypeFromProgID("Shell.Application")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var folder = shell.NameSpace(Path.GetDirectoryName(path));
            var folderItem = folder.ParseName(Path.GetFileName(path));
            return folderItem.GetLink.Path;
        } catch { return path; }
    }

    private BitmapImage LoadImageNoLock(string path)
    {
        BitmapImage bmp = new BitmapImage();
        using (var fs = File.OpenRead(path)) {
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = fs; bmp.EndInit();
        }
        bmp.Freeze(); return bmp;
    }

    private void TargetPathInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshIconDisplay();
    private void RefreshIconDisplay() {
        if (!string.IsNullOrEmpty(_customCroppedPath)) ImagePreview.Source = LoadImageNoLock(_customCroppedPath);
        else if (File.Exists(TargetPathInput.Text) || Directory.Exists(TargetPathInput.Text)) UpdateDefaultIcon(TargetPathInput.Text);
        UpdatePreviewScale(IconScaleSlider.Value);
    }
    private void ResetIcon_Click(object sender, RoutedEventArgs e) { _customCroppedPath = null; RefreshIconDisplay(); }
    private void ResetScale_Click(object sender, RoutedEventArgs e) => IconScaleSlider.Value = 33;
    private void IconScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_isUpdatingScale && IconScaleInput != null) { _isUpdatingScale = true; IconScaleInput.Text = ((int)e.NewValue).ToString(); UpdatePreviewScale(e.NewValue); _isUpdatingScale = false; } }
    private void IconScaleInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (!_isUpdatingScale && int.TryParse(IconScaleInput.Text, out int v)) { v = Math.Clamp(v, 10, 100); _isUpdatingScale = true; IconScaleSlider.Value = v; UpdatePreviewScale(v); _isUpdatingScale = false; } }
    private void UpdatePreviewScale(double p) 
    { 
        if (ImagePreview != null) 
        { 
            double s = 150.0 * (p / 100.0); 
            // 确保不会变成 0
            if (s < 1) s = 1;
            ImagePreview.Width = s; 
            ImagePreview.Height = s; 
        } 
}
    private void SelectFile_Click(object sender, RoutedEventArgs e) { var d = new OpenFileDialog(); if (d.ShowDialog() == true) { TargetPathInput.Text = d.FileName; TileNameInput.Text = Path.GetFileNameWithoutExtension(d.FileName); } }
    private void SelectFolder_Click(object sender, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) { TargetPathInput.Text = d.FolderName; TileNameInput.Text = new DirectoryInfo(d.FolderName).Name; } }
    private void SelectImage_Click(object sender, RoutedEventArgs e) {
        var d = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg" };
        if (d.ShowDialog() == true) {
            CropWindow cw = new CropWindow(d.FileName) { Owner = this };
            if (cw.ShowDialog() == true) { _customCroppedPath = cw.CroppedPath; RefreshIconDisplay(); }
        }
    }
}