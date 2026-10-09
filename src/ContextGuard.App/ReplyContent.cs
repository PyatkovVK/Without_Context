using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace ContextGuard.App;

// Presentation only: a small Markdown subset with no HTML, images, links or external resources.
public sealed class ReplyContent : StackPanel
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<ReplyContent, string>(nameof(Text), "");
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    private bool renderPending;
    public ReplyContent() { Spacing = 10; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && !renderPending)
        {
            renderPending = true;
            Dispatcher.UIThread.Post(() => { renderPending = false; RenderText(); }, DispatcherPriority.Background);
        }
    }
    private void RenderText()
    {
        Children.Clear();
        string[] lines = Text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            // Bound visual elements even when a provider returns a very large response.
            if (Children.Count >= 180) { Children.Add(Paragraph(string.Join('\n', lines.Skip(i)))); break; }
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var code = new List<string>();
                while (++i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal)) code.Add(lines[i]);
                Children.Add(new Border { Background = Brush.Parse("#0A1420"), BorderBrush = Brush.Parse("#203344"),
                    BorderThickness = new(1), CornerRadius = new(10), Padding = new(14), Child = new TextBox
                    { Text = string.Join('\n', code), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                        FontFamily = new("Cascadia Code,Consolas,Menlo,monospace"), FontSize = 12, Background = Brushes.Transparent,
                        BorderThickness = new(0), Padding = new(0), MinHeight = 0 } });
                continue;
            }
            if (i + 1 < lines.Length && line.Contains('|') && IsTableSeparator(lines[i + 1]))
            {
                var rows = new List<string[]> { Cells(line) }; i += 2;
                while (i < lines.Length && lines[i].Contains('|') && rows.Count < 48) rows.Add(Cells(lines[i++]));
                i--; Children.Add(Table(rows)); continue;
            }
            var block = Paragraph(line.TrimStart('#', ' '));
            int heading = line.TakeWhile(c => c == '#').Count();
            if (heading is > 0 and <= 6) { block.FontSize = heading <= 2 ? 18 : 15; block.FontWeight = FontWeight.SemiBold; block.Margin = new(0, 8, 0, 0); }
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                block = Paragraph("•  " + line[2..]);
            Children.Add(block);
        }
    }
    private static SelectableTextBlock Paragraph(string text)
    {
        var block = new SelectableTextBlock { FontSize = 14, LineHeight = 22, TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#DDE6F0"), Inlines = new InlineCollection() };
        int start = 0;
        while (start < text.Length)
        {
            int open = text.IndexOf("**", start, StringComparison.Ordinal);
            int close = open < 0 ? -1 : text.IndexOf("**", open + 2, StringComparison.Ordinal);
            if (close < 0) { block.Inlines.Add(new Run(text[start..])); break; }
            if (open > start) block.Inlines.Add(new Run(text[start..open]));
            block.Inlines.Add(new Run(text[(open + 2)..close]) { FontWeight = FontWeight.SemiBold });
            start = close + 2;
        }
        return block;
    }
    private static bool IsTableSeparator(string line) => line.Contains('-') && line.All(c => c is '-' or ':' or '|' or ' ' or '\t');
    private static string[] Cells(string line) => line.Trim().Trim('|').Split('|').Select(value => value.Trim()).Take(8).ToArray();
    private static Control Table(List<string[]> rows)
    {
        int columns = rows.Max(row => row.Length);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", columns))) };
        for (int row = 0; row < rows.Count; row++)
        {
            grid.RowDefinitions.Add(new(GridLength.Auto));
            for (int col = 0; col < columns; col++)
            {
                var text = Paragraph(col < rows[row].Length ? rows[row][col] : ""); text.FontSize = 12; text.LineHeight = 18;
                if (row == 0) text.FontWeight = FontWeight.SemiBold;
                var cell = new Border { Padding = new(10), BorderBrush = Brush.Parse("#243748"), BorderThickness = new(0, 0, 1, 1),
                    Background = Brush.Parse(row == 0 ? "#172A3B" : row % 2 == 0 ? "#0C1926" : "#101F2D"), Child = text };
                Grid.SetRow(cell, row); Grid.SetColumn(cell, col); grid.Children.Add(cell);
            }
        }
        return new Border { CornerRadius = new(12), BorderBrush = Brush.Parse("#243748"), BorderThickness = new(1), ClipToBounds = true, Child = grid };
    }
}
