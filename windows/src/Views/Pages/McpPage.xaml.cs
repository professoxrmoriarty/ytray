#nullable enable
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YTray.Core;

namespace YTray.Views.Pages
{
    public partial class McpPage : Page
    {
        private readonly InstanceStore _store;
        private bool _refreshing;
        private static readonly string Configuration = "{\r\n  \"servers\": {\r\n    \"ytray\": {\r\n      \"type\": \"http\",\r\n      \"url\": \"" + McpServer.Endpoint + "\"\r\n    }\r\n  }\r\n}";

        public McpPage(InstanceStore store)
        {
            InitializeComponent();
            _store = store;
            EndpointText.Text = McpServer.Endpoint;
            ConfigTextBox.Text = Configuration;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            Refresh();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _store.PropertyChanged += OnStoreChanged;
            Refresh();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => _store.PropertyChanged -= OnStoreChanged;

        private void OnStoreChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != string.Empty && e.PropertyName != nameof(InstanceStore.IsMcpAvailable)
                && e.PropertyName != nameof(InstanceStore.McpStatusMessage)
                && e.PropertyName != nameof(InstanceStore.IsMcpAutoStartEnabled)) return;
            if (Dispatcher.CheckAccess()) Refresh();
            else Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Refresh()
        {
            if (ServiceToggle == null) return;
            var running = _store.IsMcpAvailable;
            var failed = !running && _store.McpStatusMessage.StartsWith("启动失败", StringComparison.Ordinal);
            var statusBrush = (Brush)FindResource(running ? "SuccessBrush" : failed ? "DangerBrush" : "TextTertiaryBrush");
            _refreshing = true;
            try
            {
                ServiceToggle.IsChecked = running;
                AutoStartToggle.IsChecked = _store.IsMcpAutoStartEnabled;
            }
            finally { _refreshing = false; }
            HeaderStatusDot.Fill = statusBrush;
            HeaderStatusText.Text = running ? "服务运行中" : failed ? "启动失败" : "服务已停止";
            ServiceStatusTitle.Text = running ? "正在接受 Agent 连接" : failed ? "监听器启动失败" : "当前不接受连接";
            ServiceStatusDetail.Text = _store.McpStatusMessage;
            StatusPill.Background = (Brush)FindResource(running ? "SuccessPaleBrush" : failed ? "DangerPaleBrush" : "SurfaceMutedBrush");
            HeaderStatusText.Foreground = (Brush)FindResource(running ? "SuccessBrush" : failed ? "DangerBrush" : "TextSecondaryBrush");
        }

        private void ServiceToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_refreshing) return;
            if (ServiceToggle.IsChecked == true) _store.StartMcpService();
            else _store.StopMcpService();
            Refresh();
        }

        private void AutoStartToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_refreshing) _store.SetMcpAutoStart(AutoStartToggle.IsChecked == true);
        }

        private async void CopyEndpoint_Click(object sender, RoutedEventArgs e) =>
            await CopyAsync(McpServer.Endpoint, CopyEndpointButton, "复制地址");

        private async void CopyConfig_Click(object sender, RoutedEventArgs e) =>
            await CopyAsync(Configuration, CopyConfigButton, "复制配置");

        private static async Task CopyAsync(string value, Button button, string idleText)
        {
            try
            {
                Clipboard.SetText(value);
                button.Content = "已复制";
            }
            catch { button.Content = "复制失败"; }
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            button.Content = idleText;
        }
    }
}
