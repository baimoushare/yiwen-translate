using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OverTranslate.Services;
// UseWindowsForms puts System.Windows.Forms in the implicit usings, so these names collide
using UserControl  = System.Windows.Controls.UserControl;
using Button       = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OverTranslate.Views.Controls;

/// <summary>
/// 带搜索的字体选择框：本机字体数百个，普通下拉翻不过来。输入框即搜索框——聚焦全选、
/// 打字即按“显示名或字体名包含”过滤列表，点行或回车选定，Esc 撤销。
/// </summary>
/// <remarks>
/// 与 ModelBox 同一套视觉（外框 Border + 无边框编辑器 + 弹出列表），行为差异在：选项是
/// 宿主一次性注入的本机字体（<see cref="SetOptions"/>，设置页每次进页/切语言重建），
/// 无网络、无加载态；只有“无匹配”一行提示。
///
/// 搜索词匹配 <c>Display</c>（如 霞鹜文楷）或 <c>Family</c>（如 LXGW WenKai），不区分
/// 大小写——中文名和英文名各记得一半的人都能找到。
/// </remarks>
public partial class FontBox : UserControl
{
    public FontBox()
    {
        InitializeComponent();
    }

    private List<UiFontOption> _options = [];

    /// <summary>StaysOpen=False 的弹出被点击外部关闭的时刻，用来识别“点 ▾ 意在关闭”。</summary>
    private DateTime _lastClosed = DateTime.MinValue;

    /// <summary>当前选中的字体名（规范名，空串 = 跟随系统）。</summary>
    public string Family { get; private set; } = "";

    /// <summary>用户真正选定一项后触发；<see cref="SetSelected"/> 回填不触发。</summary>
    public event EventHandler? SelectionCommitted;

    /// <summary>宿主注入全部选项，首项应为“默认（跟随系统）”。</summary>
    public void SetOptions(List<UiFontOption> options)
    {
        _options = options;

        // 焦点不在搜索框时才回写文本：正在搜索的人不该被回填打断。
        if (!FontInput.IsKeyboardFocusWithin) ShowSelected();
        ApplyFilter();
    }

    /// <summary>回填当前值（加载设置、切换界面语言后），不触发提交。</summary>
    public void SetSelected(string family)
    {
        Family = family;
        ShowSelected();
    }

    private string DisplayOf(string family)
    {
        foreach (var option in _options)
            if (string.Equals(option.Family, family, StringComparison.OrdinalIgnoreCase))
                return option.Display;
        return family;
    }

    private void ShowSelected() => FontInput.Text = DisplayOf(Family);

    /// <summary>按当前输入过滤列表；空输入给全表。</summary>
    private void ApplyFilter()
    {
        ApplyFilter(FontInput.Text.Trim());
    }

    /// <param name="query">搜索词；空串给全表。</param>
    private void ApplyFilter(string query)
    {
        IEnumerable<UiFontOption> matched = query.Length == 0
            ? _options
            : _options.Where(o =>
                o.Display.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                o.Family.Contains(query, StringComparison.OrdinalIgnoreCase));

        FontList.ItemsSource = matched.ToList();
        FontList.SelectedItem = null;

        var empty = FontList.Items.Count == 0 && query.Length > 0;
        NoMatchText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ListHost.Visibility    = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 打开列表永远从全表开始：输入框里此刻显示的是“当前选中名”，不是查询词——
    /// 拿它过滤会把全机字体筛成当前字体一族，看起来就像系统字体全没了。查询只在
    /// 用户真正敲键之后（TextChanged）才生效。
    /// </summary>
    private void OpenList()
    {
        ApplyFilter("");
        ListPopup.IsOpen = true;
    }

    private void FontInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        FontInput.SelectAll();
        OpenList();
    }

    // 只在弹出打开期间把输入当搜索词；关闭状态下的程序赋值（ShowSelected/Commit/Restore）
    // 走同一条路，被这里的早退挡掉，避免选中后过滤把列表又算一遍。
    private void FontInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!ListPopup.IsOpen) return;
        ApplyFilter();
    }

    private void DropBtn_Click(object sender, RoutedEventArgs e)
    {
        // 点击 ▾ 时 StaysOpen=False 已先把弹出关了——距 Closed 极近说明这次点击就是“关闭”。
        if ((DateTime.Now - _lastClosed).TotalMilliseconds < 200) return;

        FontInput.Focus();
        FontInput.SelectAll();
        OpenList();
    }

    // 键盘：↓↑ 移动高亮，Enter 提交，Esc 撤销。焦点始终留在输入框，搜索不打断。
    private void FontInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (FontList.SelectedItem is UiFontOption option) Commit(option);
                else if (FontList.Items.Count > 0 && FontList.Items[0] is UiFontOption first)
                    Commit(first);
                e.Handled = true;
                break;
            case Key.Escape:
                ListPopup.IsOpen = false;
                Restore();
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (FontList.Items.Count == 0) return;
        if (!ListPopup.IsOpen) OpenList();

        FontList.SelectedIndex = Math.Clamp(FontList.SelectedIndex + delta, 0, FontList.Items.Count - 1);
        FontList.ScrollIntoView(FontList.SelectedItem);
    }

    private void FontList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 鼠标点行即选定。SelectedItem 清空（ApplyFilter）到达时 Added 为空，不构成提交。
        if (FontList.SelectedItem is UiFontOption option) Commit(option);
    }

    private void Commit(UiFontOption option)
    {
        Family = option.Family;
        ListPopup.IsOpen = false;
        FontInput.Text = option.Display;
        FontInput.CaretIndex = FontInput.Text.Length;
        SelectionCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void ListPopup_Closed(object sender, EventArgs e)
    {
        _lastClosed = DateTime.Now;
        Restore();
    }

    /// <summary>文本停在过滤词而非选中名时（没选就关了弹出），恢复显示选中名。</summary>
    private void Restore() => FontInput.Text = DisplayOf(Family);
}
