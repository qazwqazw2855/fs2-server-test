using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using God2.V2Launcher.Services;

namespace God2.V2Launcher;

public partial class MainWindow : Window
{
    private ClientIntegrityResult? _clientIntegrity;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await VerifyClientAsync();
    }

    private async Task VerifyClientAsync()
    {
        try
        {
            ClientStatusText.Text = "正在驗證遊戲...";
            StartGameButton.IsEnabled = false;

            var launcherDirectory = AppContext.BaseDirectory;
            var clientDirectory = launcherDirectory;

            _clientIntegrity = await ClientIntegrityService.VerifyAsync(
                clientDirectory);

            if (!_clientIntegrity.Exists)
            {
                ClientStatusText.Text = "找不到 God2_opt.exe";
                return;
            }

            if (!_clientIntegrity.IsReferenceClient)
            {
                ClientStatusText.Text = "遊戲版本不符";
                return;
            }

            ClientStatusText.Text = "原版 Client 驗證完成";
            StartGameButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ClientStatusText.Text = $"驗證失敗：{ex.Message}";
            StartGameButton.IsEnabled = false;
        }
    }

    private async void StartGame_Click(object sender, RoutedEventArgs e)
    {
        StartGameButton.IsEnabled = false;

        try
        {
            if (_clientIntegrity?.IsReferenceClient != true)
            {
                ClientStatusText.Text = "正在重新驗證 Client...";
                await VerifyClientAsync();

                if (_clientIntegrity?.IsReferenceClient != true)
                    return;
            }

            var launcherDirectory = AppContext.BaseDirectory;
            var clientDirectory = launcherDirectory;

            var endpoint = await V2EndpointService.ConfigureAsync(clientDirectory);
            if (!endpoint.Success)
            {
                ClientStatusText.Text = endpoint.Status;
                return;
            }

            ClientStatusText.Text = "正在啟動仙界傳...";

            var gamePath = Path.Combine(
                clientDirectory,
                "God2_opt.exe");

            Process.Start(new ProcessStartInfo
            {
                FileName = gamePath,
                Arguments = "52.63.34.162",
                WorkingDirectory = clientDirectory,
                UseShellExecute = true
            });

            ClientStatusText.Text = "遊戲已啟動";
            WindowState = WindowState.Minimized;
        }
        catch (Exception ex)
        {
            ClientStatusText.Text = $"啟動失敗：{ex.Message}";
        }
        finally
        {
            StartGameButton.IsEnabled =
                _clientIntegrity?.IsReferenceClient == true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
