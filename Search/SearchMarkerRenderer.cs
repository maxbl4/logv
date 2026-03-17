using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace lgv.Search;

public class SearchMarkerRenderer : IBackgroundRenderer
{
    private IReadOnlyList<SearchEngine.SearchResult> _results = [];
    private int _currentIndex = -1;

    private static readonly System.Windows.Media.Brush NormalBrush;
    private static readonly System.Windows.Media.Brush CurrentBrush;

    static SearchMarkerRenderer()
    {
        NormalBrush = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xD7, 0x00));
        NormalBrush.Freeze();
        CurrentBrush = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(0x80, 0xFF, 0x8C, 0x00));
        CurrentBrush.Freeze();
    }

    public KnownLayer Layer => KnownLayer.Background;

    public IReadOnlyList<SearchEngine.SearchResult> Results => _results;
    public int CurrentIndex => _currentIndex;

    public void Update(IReadOnlyList<SearchEngine.SearchResult> results, int currentIndex)
    {
        _results = results;
        _currentIndex = currentIndex;
    }

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (_results.Count == 0) return;

        textView.EnsureVisualLines();

        for (int i = 0; i < _results.Count; i++)
        {
            var result = _results[i];
            var brush = (i == _currentIndex) ? CurrentBrush : NormalBrush;

            try
            {
                var segment = new ICSharpCode.AvalonEdit.Document.TextSegment
                {
                    StartOffset = result.Offset,
                    Length = result.Length
                };

                var builder = new BackgroundGeometryBuilder
                {
                    CornerRadius = 2,
                    AlignToWholePixels = true
                };
                builder.AddSegment(textView, segment);
                var geo = builder.CreateGeometry();
                if (geo is not null)
                    dc.DrawGeometry(brush, null, geo);
            }
            catch
            {
                // Out of visible range — skip
            }
        }
    }
}
