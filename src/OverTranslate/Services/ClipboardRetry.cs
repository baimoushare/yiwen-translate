using System.Runtime.InteropServices;

namespace OverTranslate.Services;

/// <summary>
/// 剪贴板写入的带重试包装。
/// 剪贴板是全系统互斥的资源:截图工具、剪贴板管理器、输入法云同步等任何进程拿着锁时,
/// <c>OpenClipboard</c> 会立刻抛 <c>CLIPBRD_E_CANT_OPEN (0x800401D0)</c>。占锁方通常在几十
/// 毫秒内放手,所以短暂退避后重试基本必然成功,远好于把一次复制直接判死刑。
/// </summary>
/// <remarks>
/// 重试跑在自建的 STA 线程上而不是调用方线程上:WPF 剪贴板的 OLE 调用要求 STA,而一个从
/// 没有同步上下文的线程被 await 的调用方会把续体落到 MTA 线程池里。自己开线程还顺带把
/// 重试期间的等待从 UI 线程上挪走了。
/// </remarks>
public static class ClipboardRetry
{
    // 只有这两种 HRESULT 值得等:锁被人占着(CANT_OPEN)或内容正被置换(CANT_SET)。
    // 其它失败(如格式问题)重试也没有意义,原样抛给调用方。
    private const int ClipbrdCantOpen = unchecked((int)0x800401D0);
    private const int ClipbrdCantSet  = unchecked((int)0x800401D1);

    // 25ms 起步、每次递增 25ms,共 10 次尝试,最坏约 1.1 秒。实测占用方远早于此放手;
    // 再久还打不开就真有进程赖着锁,继续等也不会有结果。
    private const int MaxAttempts = 10;

    /// <summary>把位图写入剪贴板,占用冲突时退避重试。</summary>
    public static Task SetImageAsync(System.Windows.Media.Imaging.BitmapSource image) =>
        RunOnSta(() => WriteWithRetry(() => System.Windows.Clipboard.SetImage(image)));

    /// <summary>把文本写入剪贴板,占用冲突时退避重试。</summary>
    public static Task SetTextAsync(string text) =>
        RunOnSta(() => WriteWithRetry(() => System.Windows.Clipboard.SetText(text)));

    /// <summary>短命 STA 线程跑完整个重试循环,异常原样送回调用方 await 的地方。</summary>
    private static Task RunOnSta(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static void WriteWithRetry(Action write)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { write(); return; }
            catch (COMException ex) when (IsRetryable(ex) && attempt < MaxAttempts - 1)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }

    private static bool IsRetryable(COMException ex) =>
        ex.HResult == ClipbrdCantOpen || ex.HResult == ClipbrdCantSet;
}
