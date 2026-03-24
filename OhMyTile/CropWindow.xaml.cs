using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OhMyTile;

public partial class CropWindow : Window
{
    public string? CroppedPath { get; private set; }
    private Point _startPoint;
    private double _originX, _originY;

    public CropWindow(string imagePath)
    {
        InitializeComponent();
        DisplayImage.Source = new BitmapImage(new Uri(imagePath));
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ImageScale != null)
        {
            ImageScale.ScaleX = e.NewValue;
            ImageScale.ScaleY = e.NewValue;
        }
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double delta = e.Delta > 0 ? 0.1 : -0.1;
        double newValue = ScaleSlider.Value + delta;
        if (newValue >= ScaleSlider.Minimum && newValue <= ScaleSlider.Maximum)
        {
            ScaleSlider.Value = newValue;
        }
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(CropCanvas);
        _originX = ImageTransform.X;
        _originY = ImageTransform.Y;
        CropCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (CropCanvas.IsMouseCaptured)
        {
            Vector v = e.GetPosition(CropCanvas) - _startPoint;
            ImageTransform.X = _originX + v.X;
            ImageTransform.Y = _originY + v.Y;
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e) => CropCanvas.ReleaseMouseCapture();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. 建立 Pbgra32 渲染目标
            System.Windows.Media.Imaging.RenderTargetBitmap bmp = 
                new System.Windows.Media.Imaging.RenderTargetBitmap(300, 300, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);

            System.Windows.Media.DrawingVisual visual = new System.Windows.Media.DrawingVisual();
            using (System.Windows.Media.DrawingContext dc = visual.RenderOpen())
            {
                // 【核心修复 1】绝不用 Brushes.Transparent，手动创建“透明黑”画刷
                var transparentBlack = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0));
                dc.DrawRectangle(transparentBlack, null, new System.Windows.Rect(0, 0, 300, 300));

                // 【核心修复 2】物理内切。裁剪框向内收缩 1 像素，把 Canvas 的抗锯齿虚边直接扔掉
                dc.PushClip(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(1, 1, 298, 298)));

                System.Windows.Media.VisualBrush brush = new System.Windows.Media.VisualBrush(CropCanvas) { Stretch = System.Windows.Media.Stretch.None };
                dc.DrawRectangle(brush, null, new System.Windows.Rect(0, 0, 300, 300));

                dc.Pop();
            }
            bmp.Render(visual);

            // 2. 导出为 310x310 物理像素
            var resized = new System.Windows.Media.Imaging.TransformedBitmap(bmp, 
                new System.Windows.Media.ScaleTransform(310.0 / 300.0, 310.0 / 300.0));

            CroppedPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omt_clean_" + Guid.NewGuid().ToString("N") + ".png");
            using (var fileStream = new System.IO.FileStream(CroppedPath, System.IO.FileMode.Create))
            {
                System.Windows.Media.Imaging.PngBitmapEncoder encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(resized));
                encoder.Save(fileStream);
            }
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show("裁剪失败: " + ex.Message); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}