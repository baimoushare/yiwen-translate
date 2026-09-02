using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using NLog;

namespace OverTranslate.Services;

/// <summary>
/// Writes out the crop a screenshot translation read as empty, when asked to — the
/// screenshot-side counterpart of <see cref="Realtime.RealtimeFrameDump"/>.
/// </summary>
/// <remarks>
/// 「未检测到文字」有两类原因，日志再多也分不开：裁剪里本来就没有文字（框到了空白，或选区被映射到了
/// 框选人没有看过的地方），以及裁剪里有文字但检测器没读出来。第二类是模型问题；第一类是几何问题，
/// 而看一眼裁剪图就能定案 —— 2026-09-01 的混合 DPI 事故里，正是日志里 selection=-4634 与「裁剪读出的
/// 是应用自己的错误提示」两张证据合在一起，才把坐标换算定为根因。
///
/// 默认关闭，必须存在标记文件才开启：裁剪图是用户屏幕内容的照片。用文件而不是环境变量，理由与
/// 实时翻译的帧转储相同 —— 应用通常从快捷方式或资源管理器启动，继承不到 shell 里敲的变量；在日志
/// 目录旁建一个文件任何人都能做到，删掉即关闭。每次运行限量，开着忘了也不会写满磁盘。
/// </remarks>
internal static class CaptureFrameDump
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const int MaxDumps = 20;

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yiwen", "logs");

    private static readonly string Directory = Path.Combine(LogDirectory, "captures");

    /// <summary>创建 <c>%AppData%\Yiwen\logs\dumpcaptures</c> 后重启生效。</summary>
    public static readonly bool IsEnabled = File.Exists(Path.Combine(LogDirectory, "dumpcaptures"));

    private static int _written;

    /// <summary>
    /// 一段 OCR 读为空的裁剪。这是唯一值得落盘的形态：读到内容的裁剪没有诊断价值，而空结果的
    /// 裁剪正是「几何错了还是模型没读到」这一问题的全部证据。
    /// </summary>
    public static void SaveEmptyResult(Bitmap crop)
    {
        if (!IsEnabled || Interlocked.Increment(ref _written) > MaxDumps) return;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(
                Directory, $"{DateTime.Now:HHmmss-fff}-empty.png");

            // 克隆一份：调用方在请求结束后会释放 workBitmap，而 Save 不允许另一个线程同时释放它。
            using var copy = new Bitmap(crop);
            copy.Save(path, ImageFormat.Png);

            Log.Debug("Saved an empty-result capture crop to {Path}", path);
        }
        catch (Exception ex)
        {
            // 诊断绝不能成为翻译会话失败的原因。
            Log.Warn(ex, "Could not save an empty-result capture crop");
        }
    }
}
