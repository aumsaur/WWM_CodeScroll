using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using static WwmRedeem.NativeMethods;

namespace WwmRedeem;

// Bottom-of-screen HUD, styled like Claudy's Pulse HUD. Its buttons take clicks,
// but the window never activates, so clicking it leaves the game focused.
public partial class HudWindow : Window
{
    static readonly Brush TextBrush = Hex("#E8E8EE"), Muted = Hex("#8A8A96"), Dim = Hex("#6E6E78");
    static readonly Brush Accent = Hex("#5FC8A0"), Warn = Hex("#E0B050");

    IntPtr _hwnd;

    public event Action? ExitClicked, SoundClicked, RefreshClicked, MarkAllClicked, ForgetClicked, OpenFolderClicked;

    public HudWindow()
    {
        InitializeComponent();
        AddKey("Ctrl V", "paste");
        AddKey("Ctrl ←", "back");
        AddKey("Ctrl →", "skip");
        AddKey("Ctrl ↓", "hide");

        ExitButton.Click += (_, _) => ExitClicked?.Invoke();
        SoundButton.Click += (_, _) => SoundClicked?.Invoke();
        MoreButton.Click += (_, _) => ToggleActions();
        Card.MouseRightButtonUp += (_, e) => { ToggleActions(); e.Handled = true; };
        // Each action closes the row again once it's done.
        RefreshButton.Click += (_, _) => { ToggleActions(); RefreshClicked?.Invoke(); };
        MarkAllButton.Click += (_, _) => { ToggleActions(); MarkAllClicked?.Invoke(); };
        ForgetButton.Click += (_, _) => { ToggleActions(); ForgetClicked?.Invoke(); };
        OpenFolderButton.Click += (_, _) => { ToggleActions(); OpenFolderClicked?.Invoke(); };

        SourceInitialized += (_, _) => MakeNonActivating();
        SizeChanged += (_, _) => PlaceAtBottom();
    }

    public void SetSound(bool on)
    {
        SoundButton.Content = on ? "" : "";   // speaker / muted speaker
        SoundButton.ToolTip = on ? "Sound is on - click to mute" : "Sound is off - click to turn it on";
    }

    void ToggleActions()
    {
        bool open = ActionsPanel.Visibility != Visibility.Visible;
        ActionsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.Content = open ? "" : "";   // chevron up while open, ... when closed
    }

    public void ShowCode(string code, int position, int total, IReadOnlyList<string> upcoming)
    {
        MessagePanel.Visibility = Visibility.Collapsed;
        CodePanel.Visibility = Visibility.Visible;
        bool changed = CodeText.Text != code;
        CodeText.Text = code;
        CodeText.Foreground = TextBrush;
        CounterText.Text = $"{position} / {total}";
        PreviewText.Foreground = Dim;
        PreviewText.Text = upcoming.Count > 0 ? "next  " + string.Join("  ", upcoming) : "last one";
        if (changed) CodeText.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(180)));
    }

    // Shown in the gap between Ctrl+V and the next code being loaded.
    public void ShowPasted()
    {
        CodeText.Foreground = Accent;
        PreviewText.Foreground = Accent;
        PreviewText.Text = "✓ pasted";
    }

    public void ShowMessage(string title, string detail)
    {
        CodePanel.Visibility = Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
        MessageTitle.Text = title;
        MessageDetail.Text = detail;
    }

    public void SetSource(string text, bool warning)
    {
        SourceText.Text = "WWM Redeem · " + text;
        SourceText.Foreground = warning ? Warn : Muted;
    }

    public void SetGame(bool running)
    {
        GameDot.Fill = running ? Accent : Warn;
        GameText.Text = running ? "game running" : "start Where Winds Meet";
    }

    // Borderless games can push themselves above other topmost windows, so this is
    // re-asserted on a timer. NOACTIVATE keeps the game focused.
    public void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero && IsVisible)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void SaveSnapshot(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * 2), (int)Math.Ceiling(ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
    }

    void MakeNonActivating()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        long style = (long)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    void PlaceAtBottom()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight;
    }

    void AddKey(string keys, string label)
    {
        KeysPanel.Children.Add(new Border
        {
            Background = Hex("#2A2A31"),
            BorderBrush = Hex("#4A4A54"),
            BorderThickness = new Thickness(1, 1, 1, 2),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 0, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = keys, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Hex("#C8C8D0") },
        });
        KeysPanel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Muted,
            Margin = new Thickness(5, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    static SolidColorBrush Hex(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
