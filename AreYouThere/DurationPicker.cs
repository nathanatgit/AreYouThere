using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AreYouThere;

/// <summary>
/// 仿 Windows 时间选择器的时长选择器，多一列“秒”：平时是分成 时 | 分 | 秒 三段的按钮，
/// 点开后是三列滚动选择，滚轮一格移动一行，中间高亮的一行就是选中值，✓ 确认、✗ 取消。
/// Windows 自带的 TimePicker 只有时和分，且它内部的滚动列不能在应用里直接使用，所以自己实现。
/// </summary>
internal sealed partial class DurationPicker : Grid
{
    private const double ItemHeight = 40, ColumnWidth = 84;
    private const int VisibleRows = 9;

    private readonly Button _button;
    private readonly TextBlock[] _display = new TextBlock[3];
    private readonly TextBlock[] _headers = new TextBlock[3];
    private readonly Column[] _columns;
    private readonly Flyout _flyout;
    private readonly Grid _flyoutContent;
    // 选中行文字颜色用 ThemeResource 绑定在这个隐藏元素上，切换浅色/深色时自动更新
    private readonly TextBlock _onAccentProbe;
    private string[] _units = ["h", "min", "s"];
    private TimeSpan _value;

    public TimeSpan Minimum { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan Maximum { get; set; } = new(23, 59, 59);

    /// <summary>用户点 ✓ 改动后触发（代码设置 Value 不触发）。</summary>
    public event EventHandler<TimeSpan>? ValueChanged;

    public DurationPicker()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        _onAccentProbe = Themed<TextBlock>("<TextBlock Foreground=\"{ThemeResource TextOnAccentFillColorPrimaryBrush}\" Visibility=\"Collapsed\" />");
        _columns = [new Column(24, false, OnAccent), new Column(60, true, OnAccent), new Column(60, true, OnAccent)];
        _button = BuildButton();
        Children.Add(_button);
        _flyoutContent = BuildFlyoutContent();
        _flyoutContent.ActualThemeChanged += (_, _) => { foreach (var c in _columns) c.RefreshHighlight(); };
        _flyout = new Flyout { Content = _flyoutContent, Placement = FlyoutPlacementMode.Bottom };
        // 弹出层不会自动继承窗口里选的主题，打开时按按钮当前的主题设置
        _flyout.Opening += (_, _) =>
        {
            _flyout.FlyoutPresenterStyle = PresenterStyle(_button.ActualTheme);
            _flyoutContent.RequestedTheme = _button.ActualTheme;
        };
        _flyout.Opened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            _columns[0].ScrollTo((int)_value.TotalHours, animate: false);
            _columns[1].ScrollTo(_value.Minutes, animate: false);
            _columns[2].ScrollTo(_value.Seconds, animate: false);
        });
        _button.Click += (_, _) => _flyout.ShowAt(_button);
        UpdateDisplay();
    }

    public TimeSpan Value
    {
        get => _value;
        set
        {
            _value = Clamp(value);
            UpdateDisplay();
        }
    }

    public void SetLabels(string hours, string minutes, string seconds)
    {
        _units = [hours, minutes, seconds];
        for (var i = 0; i < 3; i++) _headers[i].Text = _units[i];
        UpdateDisplay();
    }

    private Button BuildButton()
    {
        var grid = new Grid();
        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnWidth) });
            _display[i] = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            SetColumn(_display[i], i);
            grid.Children.Add(_display[i]);
            if (i > 0) grid.Children.Add(Divider(i, vertical: true));
        }
        return new Button
        {
            Content = grid, Padding = new Thickness(0), Height = 32,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
        };
    }

    private Grid BuildFlyoutContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ItemHeight * VisibleRows) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 中间一行的高亮条，放在三列滚动区下面
        var band = Themed<Border>($"<Border Height=\"{ItemHeight}\" Margin=\"4,0,4,0\" CornerRadius=\"4\" VerticalAlignment=\"Center\" Background=\"{{ThemeResource AccentFillColorDefaultBrush}}\" />");
        SetRow(band, 1);
        SetColumnSpan(band, 3);
        root.Children.Add(band);
        root.Children.Add(_onAccentProbe);

        for (var i = 0; i < 3; i++)
        {
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnWidth) });
            // 灰色说明层级：12 号、系统次要文字颜色
            _headers[i] = Themed<TextBlock>("<TextBlock FontSize=\"12\" HorizontalAlignment=\"Center\" Margin=\"0,8,0,4\" Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\" />");
            _headers[i].Text = _units[i];
            SetColumn(_headers[i], i);
            root.Children.Add(_headers[i]);
            var viewer = _columns[i].Viewer;
            SetRow(viewer, 1);
            SetColumn(viewer, i);
            root.Children.Add(viewer);
            if (i > 0)
            {
                var divider = Divider(i, vertical: true);
                SetRow(divider, 1);
                root.Children.Add(divider);
            }
        }

        var accept = new Button { Content = new SymbolIcon(Symbol.Accept), HorizontalAlignment = HorizontalAlignment.Stretch };
        var cancel = new Button { Content = new SymbolIcon(Symbol.Cancel), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var b in new[] { accept, cancel })
        {
            b.Height = 40;
            b.Margin = new Thickness(4);
            b.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            b.BorderThickness = new Thickness(0);
        }
        accept.Click += (_, _) => Accept();
        cancel.Click += (_, _) => _flyout.Hide();
        var buttons = Themed<Grid>("<Grid BorderThickness=\"0,1,0,0\" BorderBrush=\"{ThemeResource DividerStrokeColorDefaultBrush}\" />");
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        SetColumn(cancel, 1);
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        SetRow(buttons, 2);
        SetColumnSpan(buttons, 3);
        root.Children.Add(buttons);
        return root;
    }

    private void Accept()
    {
        var picked = new TimeSpan(_columns[0].Selected, _columns[1].Selected, _columns[2].Selected);
        _flyout.Hide();
        var value = Clamp(picked);
        if (value == _value) { UpdateDisplay(); return; }
        _value = value;
        UpdateDisplay();
        ValueChanged?.Invoke(this, value);
    }

    private void UpdateDisplay()
    {
        _display[0].Text = $"{(int)_value.TotalHours} {_units[0]}";
        _display[1].Text = $"{_value.Minutes:00} {_units[1]}";
        _display[2].Text = $"{_value.Seconds:00} {_units[2]}";
    }

    private TimeSpan Clamp(TimeSpan value) => value < Minimum ? Minimum : value > Maximum ? Maximum : value;

    private static Rectangle Divider(int column, bool vertical)
    {
        var line = Themed<Rectangle>("<Rectangle Width=\"1\" HorizontalAlignment=\"Left\" Fill=\"{ThemeResource DividerStrokeColorDefaultBrush}\" />");
        SetColumn(line, column);
        return line;
    }

    private static Style PresenterStyle(ElementTheme theme)
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, double.PositiveInfinity));
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
        return style;
    }

    private Brush OnAccent() => _onAccentProbe.Foreground;

    /// <summary>
    /// 用 XAML 创建带 ThemeResource 的元素：这样颜色会跟随元素实际的浅色/深色主题并在切换时更新；
    /// 在代码里直接取 Application.Current.Resources 只能拿到启动时的主题颜色。
    /// </summary>
    private static T Themed<T>(string xaml) where T : class =>
        (T)XamlReader.Load(xaml.Insert(xaml.IndexOf(' '), " xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\""));

    /// <summary>一列滚动选择：上下留出半屏空白，让第一个和最后一个数字也能滚到中间。</summary>
    private sealed class Column
    {
        public ScrollViewer Viewer { get; }
        public int Selected { get; private set; }
        private readonly TextBlock[] _items;
        private readonly Func<Brush> _onAccent;

        public Column(int count, bool pad, Func<Brush> onAccent)
        {
            _onAccent = onAccent;
            var panel = new StackPanel { Padding = new Thickness(0, ItemHeight * (VisibleRows / 2), 0, ItemHeight * (VisibleRows / 2)) };
            _items = new TextBlock[count];
            for (var i = 0; i < count; i++)
            {
                var index = i;
                _items[i] = new TextBlock
                {
                    Text = pad ? i.ToString("00") : i.ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var cell = new Border
                {
                    Height = ItemHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Child = _items[i]
                };
                cell.Tapped += (_, _) => ScrollTo(index, animate: true);
                panel.Children.Add(cell);
            }
            Viewer = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Viewer.ViewChanged += (_, e) =>
            {
                Highlight((int)Math.Round(Viewer.VerticalOffset / ItemHeight));
                if (!e.IsIntermediate) Snap();
            };
            // 滚轮一格只移动一行，并吃掉事件，不让 ScrollViewer 按像素滚动
            panel.PointerWheelChanged += (_, e) =>
            {
                var delta = e.GetCurrentPoint(panel).Properties.MouseWheelDelta;
                if (delta != 0) ScrollTo(Selected - Math.Sign(delta), animate: true);
                e.Handled = true;
            };
            Highlight(0);
        }

        public void ScrollTo(int index, bool animate)
        {
            index = Math.Clamp(index, 0, _items.Length - 1);
            Highlight(index);
            Viewer.ChangeView(null, index * ItemHeight, null, !animate);
        }

        // 拖动或触摸滑动结束后对齐到最近的一行
        private void Snap()
        {
            var index = Math.Clamp((int)Math.Round(Viewer.VerticalOffset / ItemHeight), 0, _items.Length - 1);
            if (Math.Abs(Viewer.VerticalOffset - index * ItemHeight) > 0.5) Viewer.ChangeView(null, index * ItemHeight, null);
            Highlight(index);
        }

        private void Highlight(int index)
        {
            index = Math.Clamp(index, 0, _items.Length - 1);
            var brush = _onAccent();
            if (index == Selected && _items[index].Foreground == brush) return;
            _items[Selected].ClearValue(TextBlock.ForegroundProperty);
            Selected = index;
            _items[index].Foreground = brush;
        }

        /// <summary>主题切换后选中行的文字颜色要换成新主题的颜色。</summary>
        public void RefreshHighlight() => _items[Selected].Foreground = _onAccent();
    }
}
