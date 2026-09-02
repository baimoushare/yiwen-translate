using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OverTranslate.Services;
// UseWindowsForms puts System.Windows.Forms in the implicit usings, so these names collide
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OverTranslate.Views.Shell;

/// <summary>
/// Modal "about" panel drawn on top of the shell instead of in its own window, so the app
/// keeps a single visible surface. Dismissed by the close button, the scrim, or Escape.
/// </summary>
public partial class AboutOverlay : UserControl
{
    private const string GitHubUrl = "https://github.com/baimoushare/yiwen-translate";

    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(140));

    public AboutOverlay()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        VersionText.Text = LocalizationService.Format("S.About.Version", version);
    }

    public void Open()
    {
        Visibility = Visibility.Visible;

        // 上次检查的状态行不跨次保留：重新打开时归零，按钮恢复可用
        UpdateStatusText.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "";
        CheckUpdateBtn.IsEnabled = true;

        // WPF switches text off pixel snapping as soon as it detects the text is being animated,
        // then ramps snapping back on over roughly a second once the motion stops — which is why
        // the card used to reach its final size and stay soft for a beat before turning sharp, and
        // there is no API to shorten or disable that ramp. Rendering the card into a bitmap cache
        // for the duration of the scale sidesteps it: the glyphs are rasterised once as static,
        // snapped text and the render thread only scales the finished bitmap, so the detector never
        // sees animating text. The cache is dropped again in ReleaseAnimations.
        Card.CacheMode = new BitmapCache { SnapsToDevicePixels = true };

        var fade = new DoubleAnimation { From = 0, To = 1, Duration = FadeDuration };
        fade.Completed += (_, _) => ReleaseAnimations();
        BeginAnimation(OpacityProperty, fade);

        // Slight scale-up so the card reads as coming forward rather than blinking in
        var grow = new DoubleAnimation
        {
            From = 0.96, To = 1,
            Duration = FadeDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        // Focus lets the control receive Escape without the page underneath stealing it
        Focus();
    }

    public void Close()
    {
        var fade = new DoubleAnimation { From = 1, To = 0, Duration = FadeDuration };
        fade.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            ReleaseAnimations();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    // DoubleAnimation defaults to FillBehavior.HoldEnd, so the animated properties stay under the
    // animation clock's control long after the animation has visually finished, which keeps the
    // card in an intermediate composition layer indefinitely. Handing the properties back to their
    // owners drops that layer as soon as the transition is over.
    private void ReleaseAnimations()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;

        // Back to rendering the live visual tree — the cache existed only for the transition.
        Card.CacheMode = null;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();

    // Clicks inside the card must not bubble up to the scrim's dismiss handler
    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void GitHubBtn_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });

    /// <summary>
    /// 手动"检查更新"。自动更新只有启动弹窗和每小时静默轮询，这里是用户主动询问的入口。
    /// </summary>
    /// <remarks>
    /// 与启动检查不同，这里不受"跳过此版本"约束——用户主动来问，就把找到的结果摆出来。
    /// CheckAsync 内部已吞掉网络异常并返回 null（三种情况都算"没发现"），try/catch 只是给
    /// async void 事件处理器兜底，防止窗口构建类的意外异常直接带崩进程。
    /// </remarks>
    private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        UpdateStatusText.Text = LocalizationService.Get("S.About.UpdateChecking");
        UpdateStatusText.Visibility = Visibility.Visible;
        try
        {
            var info = await UpdateNotifier.CheckAsync();
            if (info is null)
            {
                // null = 已是最新 / 便携或未安装构建 / 检查失败，三者对用户的表现一致
                UpdateStatusText.Text = LocalizationService.Get("S.About.UpdateUpToDate");
                return;
            }

            // 发现新版本：关掉关于弹层，让更新窗口站到台前
            Close();
            UpdateWindow.ShowOrActivate(info);
        }
        catch
        {
            UpdateStatusText.Text = LocalizationService.Get("S.About.UpdateCheckFailed");
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
        }
    }
}
