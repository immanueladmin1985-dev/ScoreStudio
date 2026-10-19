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
