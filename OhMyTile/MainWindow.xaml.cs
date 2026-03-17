using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.UI.StartScreen;
using Windows.Storage;

namespace OhMyTile;

public partial class MainWindow : Window
{
    private string? _selectedImagePath;

    public MainWindow()
    {
        // 核心修复：防止打包后找不到 DLL 导致的闪退
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        InitializeComponent();

        BgColorInput.TextChanged += (s, e) => {
            try {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(BgColorInput.Text);
                TilePreview.Background = new System.Windows.Media.SolidColorBrush(color);
            } catch { }
        };
    }

    private void SelectImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog() == true)
        {
            _selectedImagePath = dialog.FileName;
            ImagePreview.Source = new BitmapImage(new Uri(_selectedImagePath));
        }
    }

    private async void CreateTile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_selectedImagePath))
            {
                MessageBox.Show("请先选择图片！");
                return;
            }

            var hWnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            string tileId = "Tile" + DateTime.Now.Ticks.ToString().Substring(12);

            // 1. 获取包的 LocalFolder 存储路径 (只有打包应用可用)
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            
            // 2. 将图片拷贝到 LocalFolder
            StorageFile sourceFile = await StorageFile.GetFileFromPathAsync(_selectedImagePath);
            StorageFile savedFile = await sourceFile.CopyAsync(localFolder, tileId + ".png", NameCollisionOption.ReplaceExisting);

            // 3. 使用官方协议 ms-appdata:///local/
            Uri logoUri = new Uri("ms-appdata:///local/" + savedFile.Name);

            SecondaryTile tile = new SecondaryTile(
                tileId, 
                TileNameInput.Text, 
                "args=" + tileId, 
                logoUri, 
                TileSize.Square150x150
            );

            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(BgColorInput.Text);
            tile.VisualElements.BackgroundColor = Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);
            tile.VisualElements.ShowNameOnSquare150x150Logo = true;

            WinRT.Interop.InitializeWithWindow.Initialize(tile, hWnd);
            await tile.RequestCreateAsync();
        }
        catch (InvalidOperationException)
        {
            MessageBox.Show("错误：该功能必须在‘打包身份’下运行。\n请确保你是从开始菜单启动程序的，而不是直接双击 exe。");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"失败：{ex.Message}");
        }
    }
}