using System.Windows;
using System.Windows.Controls;

namespace ScoreStudio;

/// <summary>Small windows that ask for the text of an expression or the speed of a metronome mark (Expression tool).</summary>
public static class MarkPrompts
{
    /// <summary>Beat units offered for a metronome mark, with how they are shown.</summary>
    private static readonly (string Unit, int Dots, string Name)[] Beats =
    {
        ("quarter", 0, "4분음표"), ("quarter", 1, "점4분음표"), ("half", 0, "2분음표"), ("half", 1, "점2분음표"),
        ("eighth", 0, "8분음표"), ("eighth", 1, "점8분음표"), ("whole", 0, "온음표"),
    };

    /// <summary>Asks for expression text such as "dolce" or "rit."; null when cancelled.</summary>
    public static (string Text, bool Italic, bool Bold)? AskText(Window owner)
    {
        var text = new TextBox { MinWidth = 260, Margin = new Thickness(0, 4, 0, 8) };
        var italic = new CheckBox { Content = "기울임(이탤릭)", IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
        var bold = new CheckBox { Content = "굵게(빠르기말 등)", Margin = new Thickness(0, 0, 0, 8) };
        var ok = Show(owner, "글자 넣기", new UIElement[]
        {
            new TextBlock { Text = "보표 위에 넣을 글자 (예: dolce, rit., a tempo, Andante). 비우면 그 자리의 글자를 지웁니다." },
            text, italic, bold,
        }, text);
        return ok ? (text.Text, italic.IsChecked == true, bold.IsChecked == true) : null;
    }

    /// <summary>Asks for a metronome mark's beat unit and speed; null when cancelled.</summary>
    public static (string Unit, int Dots, string PerMinute)? AskTempo(Window owner)
    {
        var beat = new ComboBox { Margin = new Thickness(0, 4, 0, 8), MinWidth = 160 };
        foreach (var (_, _, name) in Beats) beat.Items.Add(name);
        beat.SelectedIndex = 0;
        var speed = new TextBox { MinWidth = 120, Text = "120", Margin = new Thickness(0, 4, 0, 8) };
        var ok = Show(owner, "메트로놈 표시 넣기", new UIElement[]
        {
            new TextBlock { Text = "박 단위" }, beat,
            new TextBlock { Text = "1분에 몇 박인지 (예: 120, 60-66). 비우면 그 자리의 메트로놈 표시를 지웁니다." }, speed,
        }, speed);
        if (!ok) return null;
        var (unit, dots, _) = Beats[Math.Max(0, beat.SelectedIndex)];
        return (unit, dots, speed.Text);
    }

    /// <summary>Asks for fingerings ("1 3 5", "4-1") and whether they go below; null when cancelled.</summary>
    public static (string Text, bool Above)? AskFingering(Window owner)
    {
        var text = new TextBox { MinWidth = 260, Margin = new Thickness(0, 4, 0, 8) };
        var below = new CheckBox { Content = "음표 아래에 쓰기", Margin = new Thickness(0, 0, 0, 8) };
        var ok = Show(owner, "운지 번호 넣기", new UIElement[]
        {
            new TextBlock { Text = "화음의 아래 음부터 띄어 씁니다(예: 1 3 5). 바꿔 짚기는 4-1처럼 씁니다. 비우면 지웁니다." },
            text, below,
        }, text);
        return ok ? (text.Text, below.IsChecked != true) : null;
    }

    /// <summary>Asks for a rehearsal mark's text; empty takes the next letter. Null when cancelled.</summary>
    public static string? AskRehearsal(Window owner)
    {
        var text = new TextBox { MinWidth = 160, Margin = new Thickness(0, 4, 0, 8) };
        var ok = Show(owner, "리허설 마크 넣기", new UIElement[]
        {
            new TextBlock { Text = "네모 안에 쓸 글자 (예: A, 12, Intro). 비우면 앞 마크의 다음 글자를 씁니다." }, text,
        }, text);
        return ok ? text.Text : null;
    }

    /// <summary>Asks for one line of text with a hint; null when cancelled.</summary>
    public static string? AskLine(Window owner, string title, string hint, string initial = "")
    {
        var text = new TextBox { MinWidth = 260, Text = initial, Margin = new Thickness(0, 4, 0, 8) };
        return Show(owner, title, new UIElement[] { new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 }, text }, text) ? text.Text : null;
    }

    /// <summary>Asks to choose one of a list (and, when asked, whether it goes below); null when cancelled.</summary>
    public static (int Index, bool Below)? AskChoice(Window owner, string title, string hint, IEnumerable<string> choices, bool askBelow = false)
    {
        var list = new ComboBox { MinWidth = 300, Margin = new Thickness(0, 4, 0, 8) };
        foreach (var choice in choices) list.Items.Add(choice);
        list.SelectedIndex = 0;
        var below = new CheckBox { Content = "보표 아래에", Margin = new Thickness(0, 0, 0, 8), Visibility = askBelow ? Visibility.Visible : Visibility.Collapsed };
        var focus = new TextBox { Visibility = Visibility.Collapsed };
        var ok = Show(owner, title, new UIElement[] { new TextBlock { Text = hint }, list, below, focus }, focus);
        return ok && list.SelectedIndex >= 0 ? (list.SelectedIndex, below.IsChecked == true) : null;
    }

    private static bool Show(Window owner, string title, IEnumerable<UIElement> rows, TextBox focus)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        foreach (var row in rows) panel.Children.Add(row);
        var window = new Window
        {
            Title = title, Owner = owner, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okButton = new Button { Content = "확인", IsDefault = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
        okButton.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(okButton);
        buttons.Children.Add(new Button { Content = "취소", IsCancel = true, Padding = new Thickness(16, 4, 16, 4) });
        panel.Children.Add(buttons);

        window.Loaded += (_, _) =>
        {
            focus.Focus();
            focus.SelectAll();
        };
        return window.ShowDialog() == true;
    }
}
