using PiSignalWatch.Collectors;
using Xunit;

public class WebPageCollectorTests
{
    [Fact]
    public void CleanHtmlContentRemovesNonContentBlocksAndCollapsesWhitespace()
    {
        var html = """
        <html>
          <head>
            <style>.hidden { display:none; }</style>
            <script>console.log('ignore me');</script>
            <title>Ignored by content cleaner</title>
          </head>
          <body>
            <!-- remove this comment -->
            <main>
              Hello&nbsp;&nbsp;
              <strong>world</strong>!
              <noscript>Enable JavaScript</noscript>
              <p>Next\nline</p>
            </main>
          </body>
        </html>
        """;

        var result = WebPageCollector.CleanHtmlContent(html);

        Assert.Equal("Hello world ! Next line", result);
        Assert.DoesNotContain("console", result);
        Assert.DoesNotContain("display", result);
        Assert.DoesNotContain("Ignored by content cleaner", result);
        Assert.DoesNotContain("  ", result);
    }

    [Fact]
    public void CleanHtmlContentRemovesActualNewlinesAndSurroundingExtraSpaces()
    {
        var html = "<article> First\n\n   second\t third   </article>";

        var result = WebPageCollector.CleanHtmlContent(html);

        Assert.Equal("First second third", result);
    }
}
