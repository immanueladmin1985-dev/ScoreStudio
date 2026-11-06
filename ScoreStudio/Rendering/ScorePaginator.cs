using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ScoreStudio.Layout;

namespace ScoreStudio.Rendering;

/// <summary>The laid-out pages for printing, drawn by <see cref="PageRenderer"/> at their own size in mm.</summary>
public sealed class ScorePaginator : DocumentPaginator
{
    private readonly ScoreLayout _layout;

    public ScorePaginator(ScoreLayout layout)
    {
        _layout = layout;
        var first = layout.Pages.FirstOrDefault();
        PageSize = first is null ? new Size(210 * PageRenderer.DipPerMm, 297 * PageRenderer.DipPerMm)
            : new Size(first.Width * PageRenderer.DipPerMm, first.Height * PageRenderer.DipPerMm);
    }

    public override bool IsPageCountValid => true;
    public override int PageCount => _layout.Pages.Count;
    public override Size PageSize { get; set; }
    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        var page = _layout.Pages[pageNumber];
        var size = new Size(page.Width * PageRenderer.DipPerMm, page.Height * PageRenderer.DipPerMm);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            PageRenderer.Render(dc, page, 1.0);
        return new DocumentPage(visual, size, new Rect(size), new Rect(size));
    }
}
