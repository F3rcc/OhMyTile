using System;
using System.IO;
using System.Drawing; // 仅用于 GenerateTileImage 中的 GDI+ 逻辑
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls; // 必须有：解决 Canvas 不存在
using System.Windows.Interop;
using System.Windows.Media; // 必须有：解决 SolidColorBrush 不存在
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.UI.StartScreen;
using Windows.Storage;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace OhMyTile;

public partial class MainWindow : Window
{
    private bool _isUpdatingScale = false;
    private string? _customCroppedPath; 
    private string? _extractedIconPath; 

    // HSV 状态变量
    private double _currentH = 0; 
    private double _currentS = 1; 
    private double _currentV = 1;

    // --- Win32 API 声明 (用于图标提取与全局吸色) ---
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")]
    static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out System.Drawing.Point lpPoint);
    [ComImport][Guid("4605c42f-4456-4732-a59d-5381d1b90768")][InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IImageList { [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon); }

    public MainWindow()
    {
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        InitializeComponent();

        // 初始化色环
        CreateColorWheel();
        UpdateSelectedColor();
        
        this.Loaded += (s, e) => UpdatePreviewScale(IconScaleSlider.Value);
    }

    // --- 核心修复：色环生成 ---
    private void CreateColorWheel()
    {
        int size = 200;
        var bmp = new WriteableBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        int[] pixels = new int[size * size];

        double centerX = 100, centerY = 100;
        double outerR = 98, innerR = 78;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - centerX;
                double dy = y - centerY;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= outerR && dist >= innerR)
                {
                    double angle = Math.Atan2(dy, dx) * (180 / Math.PI) + 180;
                    var c = ColorFromHsv(angle, 1, 1);
                    pixels[y * size + x] = (c.A << 24) | (c.R << 16) | (c.G << 8) | (c.B);
                }
            }
        }
        bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        ColorWheelImg.Source = bmp;
    }

    // --- 核心修复：HSV 转 WPF Color ---
    private System.Windows.Media.Color ColorFromHsv(double h, double s, double v)
    {
        int hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
        double f = h / 60 - Math.Floor(h / 60);
        byte vVal = (byte)(v * 255);
        byte p = (byte)(v * 255 * (1 - s));
        byte q = (byte)(v * 255 * (1 - f * s));
        byte t = (byte)(v * 255 * (1 - (1 - f) * s));

        return hi switch {
            0 => System.Windows.Media.Color.FromRgb(vVal, t, p),
            1 => System.Windows.Media.Color.FromRgb(q, vVal, p),
            2 => System.Windows.Media.Color.FromRgb(p, vVal, t),
            3 => System.Windows.Media.Color.FromRgb(p, q, vVal),
            4 => System.Windows.Media.Color.FromRgb(t, p, vVal),
            _ => System.Windows.Media.Color.FromRgb(vVal, p, q),
        };
    }

    private void UpdateSelectedColor()
    {
        var color = ColorFromHsv(_currentH, _currentS, _currentV);
        
        // 更新预览和方块底色
        if (TilePreview != null) TilePreview.Background = new SolidColorBrush(color);
        if (HueBackground != null) HueBackground.Fill = new SolidColorBrush(ColorFromHsv(_currentH, 1, 1));

        // --- 关键修改：只有当用户没在输入框里打字时，才去改输入框的字 ---
        if (!BgColorInput.IsFocused)
        {
            _isUpdatingScale = true; 
            BgColorInput.Text = color.ToString().ToUpper(); // 统一大写
            _isUpdatingScale = false;
        }

        // 更新指示器位置
        double angleRad = (_currentH - 180) * (Math.PI / 180);
        Canvas.SetLeft(HueSelector, 100 + Math.Cos(angleRad) * 88 - 7);
        Canvas.SetTop(HueSelector, 100 + Math.Sin(angleRad) * 88 - 7);
        Canvas.SetLeft(SVSelector, _currentS * 100 - 5);
        Canvas.SetTop(SVSelector, (1 - _currentV) * 100 - 5);
    }

    // --- 核心修复：添加缺失的吸色逻辑 ---
    private void StartEyeDropper_Click(object sender, RoutedEventArgs e)
    {
        this.Cursor = Cursors.Cross;
        Window mask = new Window {
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)),
            Topmost = true, Left = 0, Top = 0,
            Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight,
            Cursor = Cursors.Cross
        };

        mask.MouseDown += (s, ev) => {
            System.Drawing.Point p; GetCursorPos(out p);
            IntPtr hdc = GetDC(IntPtr.Zero);
            uint pixel = GetPixel(hdc, p.X, p.Y);
            ReleaseDC(IntPtr.Zero, hdc);

            var color = System.Windows.Media.Color.FromRgb((byte)(pixel & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)((pixel >> 16) & 0xFF));
            BgColorInput.Text = color.ToString();
            mask.Close();
            this.Cursor = Cursors.Arrow;
        };
        mask.Show();
    }

    // 限制用户只能输入 0-9 和 A-F
    private void BgColorInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // 正则表达式：只匹配十六进制字符
        System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("[^0-9a-fA-F]+");
        e.Handled = regex.IsMatch(e.Text);
    }

    private void BgColorInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingScale) return;

        string text = BgColorInput.Text;

        // --- 核心限制：强制以 # 开头 ---
        if (!text.StartsWith("#"))
        {
            _isUpdatingScale = true;
            // 如果用户删掉了 #，我们补回来，并把光标移到最后
            BgColorInput.Text = "#" + text.Replace("#", "");
            BgColorInput.SelectionStart = BgColorInput.Text.Length;
            _isUpdatingScale = false;
            return;
        }

        // --- 自动转大写（提升美观度） ---
        if (text.Any(char.IsLower))
        {
            _isUpdatingScale = true;
            int caretIndex = BgColorInput.SelectionStart;
            BgColorInput.Text = text.ToUpper();
            BgColorInput.SelectionStart = caretIndex;
            _isUpdatingScale = false;
        }

        // --- 原有的颜色同步逻辑 ---
        // 只有当长度为 7 (#RRGGBB) 或 9 (#AARRGGBB) 时才尝试同步
        if (text.Length == 7 || text.Length == 9)
        {
            try 
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text);
                
                if (TilePreview != null) TilePreview.Background = new SolidColorBrush(color);

                // 同步 HSV 变量 (保持之前的逻辑)
                UpdateHsvFromRgb(color);

                // 更新色环小圆圈位置
                UpdateSelectorsOnly();
            } 
            catch { /* 格式不完整时不处理 */ }
        }
    }

// 提取出来的辅助方法：从 RGB 更新内部 HSV 变量
private void UpdateHsvFromRgb(System.Windows.Media.Color color)
{
    double max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255.0;
    double min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255.0;
    double delta = max - min;

    if (delta > 0) {
        if (max == color.R / 255.0) _currentH = 60 * (((color.G - color.B) / 255.0 / delta) % 6);
        else if (max == color.G / 255.0) _currentH = 60 * (((color.B - color.R) / 255.0 / delta) + 2);
        else _currentH = 60 * (((color.R - color.G) / 255.0 / delta) + 4);
        if (_currentH < 0) _currentH += 360;
    }
    _currentS = max == 0 ? 0 : delta / max;
    _currentV = max;
}

// 新增：专门用于同步圆圈位置，但不改写文字
private void UpdateSelectorsOnly()
{
    if (HueSelector == null || SVSelector == null) return;
    
    double angleRad = (_currentH - 180) * (Math.PI / 180);
    Canvas.SetLeft(HueSelector, 100 + Math.Cos(angleRad) * 88 - 7);
    Canvas.SetTop(HueSelector, 100 + Math.Sin(angleRad) * 88 - 7);
    Canvas.SetLeft(SVSelector, _currentS * 100 - 5);
    Canvas.SetTop(SVSelector, (1 - _currentV) * 100 - 5);
    
    if (HueBackground != null)
        HueBackground.Fill = new SolidColorBrush(ColorFromHsv(_currentH, 1, 1));
}

    // 鼠标点击拖动色环处理
    private void ColorWheel_MouseDown(object sender, MouseButtonEventArgs e) { ColorWheelImg.CaptureMouse(); HandleWheel(e); }
    private void ColorWheel_MouseMove(object sender, MouseEventArgs e) { if (ColorWheelImg.IsMouseCaptured) HandleWheel(e); }
    private void ColorWheel_MouseUp(object sender, MouseButtonEventArgs e) => ColorWheelImg.ReleaseMouseCapture();
    private void HandleWheel(MouseEventArgs e) {
        var p = e.GetPosition(ColorWheelImg);
        _currentH = Math.Atan2(p.Y - 100, p.X - 100) * (180 / Math.PI) + 180;
        UpdateSelectedColor();
    }
    private void SVPad_MouseDown(object sender, MouseButtonEventArgs e) { ((FrameworkElement)sender).CaptureMouse(); HandleSV(e); }
    private void SVPad_MouseMove(object sender, MouseEventArgs e) { if (((FrameworkElement)sender).IsMouseCaptured) HandleSV(e); }
    private void SVPad_MouseUp(object sender, MouseButtonEventArgs e) => ((FrameworkElement)sender).ReleaseMouseCapture();
    private void HandleSV(MouseEventArgs e) {
        var p = e.GetPosition(HueBackground);
        _currentS = Math.Clamp(p.X / 100.0, 0, 1);
        _currentV = Math.Clamp(1 - (p.Y / 100.0), 0, 1);
        UpdateSelectedColor();
    }

    // --- 磁贴生成逻辑 (保持之前成功的高清无白边版本) ---
    private void UpdateDefaultIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try {
            string realPath = GetShortcutTarget(path);
            IntPtr[] phicon = new IntPtr[1]; uint[] piconid = new uint[1];
            PrivateExtractIcons(realPath, 0, 256, 256, phicon, piconid, 1, 0);
            if (phicon[0] != IntPtr.Zero) {
                using (System.Drawing.Icon icon = System.Drawing.Icon.FromHandle(phicon[0]))
                using (Bitmap rawBmp = icon.ToBitmap()) {
                    rawBmp.SetResolution(96f, 96f);
                    IntPtr hBitmap = rawBmp.GetHbitmap();
                    ImagePreview.Source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    DeleteObject(hBitmap);
                    string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OhMyTile");
                    if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                    _extractedIconPath = Path.Combine(cacheDir, "master_source.png");
                    rawBmp.Save(_extractedIconPath, ImageFormat.Png);
                }
                DestroyIcon(phicon[0]);
                UpdatePreviewScale(IconScaleSlider.Value);
            }
        } catch { }
    }

    private void GenerateTileImage(string sourcePath, string targetPath, double scalePercent)
    {
        const int canvasSize = 310;
        using (var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read))
        using (var sourceImg = System.Drawing.Image.FromStream(fs))
        using (var destBmp = new Bitmap(canvasSize, canvasSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            destBmp.SetResolution(96f, 96f);
            using (var g = Graphics.FromImage(destBmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(0, 0, 0, 0));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                int iconSize = (int)Math.Round(canvasSize * (scalePercent / 100.0));
                if (iconSize % 2 != 0) iconSize--;
                int offset = (canvasSize - iconSize) / 2;
                using (var attr = new ImageAttributes()) {
                    attr.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(sourceImg, new Rectangle(offset, offset, iconSize, iconSize), 0, 0, sourceImg.Width, sourceImg.Height, GraphicsUnit.Pixel, attr);
                }
            }
            destBmp.Save(targetPath, ImageFormat.Png);
        }
    }

    private async void CreateCustomLauncherTile_Click(object sender, RoutedEventArgs e)
    {
        try {
            string? imageToUse = _customCroppedPath ?? _extractedIconPath;
            if (string.IsNullOrEmpty(imageToUse)) return;
            string uniqueId = "Tile" + DateTime.Now.Ticks.ToString().Substring(10);
            string encodedTarget = Convert.ToBase64String(Encoding.UTF8.GetBytes(TargetPathInput.Text));
            string launchArgs = $"omt_action=open&target={encodedTarget}";
            string fileName = uniqueId + ".png";
            string fullPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, fileName);
            GenerateTileImage(imageToUse, fullPath, IconScaleSlider.Value);
            await Task.Delay(200);
            SecondaryTile tile = new SecondaryTile(uniqueId, TileNameInput.Text, launchArgs, new Uri("ms-appdata:///local/" + fileName), TileSize.Square150x150);
            tile.VisualElements.ShowNameOnSquare150x150Logo = true;
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(BgColorInput.Text);
            tile.VisualElements.BackgroundColor = Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B);
            tile.VisualElements.ForegroundText = ForegroundText.Light;
            WinRT.Interop.InitializeWithWindow.Initialize(tile, new WindowInteropHelper(this).Handle);
            await tile.RequestCreateAsync();
        } catch (Exception ex) { MessageBox.Show("失败: " + ex.Message); }
    }

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
        using (var fs = File.OpenRead(path)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = fs; bmp.EndInit(); }
        bmp.Freeze(); return bmp;
    }

    private void TargetPathInput_TextChanged(object sender, TextChangedEventArgs e) => RefreshIconDisplay();
    private void RefreshIconDisplay() {
        if (!string.IsNullOrEmpty(_customCroppedPath)) ImagePreview.Source = LoadImageNoLock(_customCroppedPath);
        else if (File.Exists(TargetPathInput.Text) || Directory.Exists(TargetPathInput.Text)) UpdateDefaultIcon(TargetPathInput.Text);
        UpdatePreviewScale(IconScaleSlider.Value);
    }
    private void ResetIcon_Click(object sender, RoutedEventArgs e) { _customCroppedPath = null; RefreshIconDisplay(); }
    private void ResetScale_Click(object sender, RoutedEventArgs e) => IconScaleSlider.Value = 33;
    private void IconScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_isUpdatingScale && IconScaleInput != null) { _isUpdatingScale = true; IconScaleInput.Text = ((int)e.NewValue).ToString(); UpdatePreviewScale(e.NewValue); _isUpdatingScale = false; } }
    private void IconScaleInput_TextChanged(object sender, TextChangedEventArgs e) { if (!_isUpdatingScale && int.TryParse(IconScaleInput.Text, out int v)) { v = Math.Clamp(v, 10, 100); _isUpdatingScale = true; IconScaleSlider.Value = v; UpdatePreviewScale(v); _isUpdatingScale = false; } }
    private void UpdatePreviewScale(double p) { if (ImagePreview != null) { double s = 150.0 * (p / 100.0); ImagePreview.Width = s; ImagePreview.Height = s; } }
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