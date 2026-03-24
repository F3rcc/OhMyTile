using System;
using System.Diagnostics;
using System.Windows;
using System.Text;

namespace OhMyTile;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        System.IO.Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        base.OnStartup(e);
        string[] args = e.Args;
        if (args.Length > 0)
        {
            foreach (var arg in args)
            {
                if (arg.Contains("omt_action=open"))
                {
                    HandleTileActivation(arg);
                    Current.Shutdown();
                    return;
                }
            }
        }
        base.OnStartup(e);
    }

    private void HandleTileActivation(string args)
    {
        try
        {
            // 修正解析逻辑，处理可能存在的参数包裹
            string key = "target=";
            int startIndex = args.IndexOf(key) + key.Length;
            string base64 = args.Substring(startIndex);

            byte[] data = Convert.FromBase64String(base64);
            string target = Encoding.UTF8.GetString(data);

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true 
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败: " + ex.Message);
        }
    }
}