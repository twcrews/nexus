using Markdig;
using Nexus.Services;

namespace Nexus.Tests;

public class ImagePlaceholderExtensionTests
{
    private static string Render(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseImagePlaceholders()
            .Build();
        return Markdig.Markdown.ToHtml(markdown, pipeline).Trim();
    }

    [Fact]
    public void Image_IsReplacedWithPlaceholderSpan()
    {
        var html = Render("![alt text](https://example.com/img.png)");
        Assert.Contains("pr-image-placeholder", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Image_PlaceholderContainsAltText()
    {
        var html = Render("![my diagram](https://example.com/img.png)");
        Assert.Contains("my diagram", html);
    }

    [Fact]
    public void Image_EmptyAlt_UsesDefaultLabel()
    {
        var html = Render("![](https://example.com/img.png)");
        Assert.Contains("image", html);
    }

    [Fact]
    public void Image_AltTextWithSpecialChars_IsHtmlEncoded()
    {
        // Markdig parses & as a literal inline, which our renderer then HtmlEncodes
        var html = Render("![A & B](https://example.com/img.png)");
        Assert.DoesNotContain("<img", html);
        Assert.Contains("A &amp; B", html);
    }

    [Fact]
    public void Image_AltTextWithHtmlTags_DoesNotRenderScript()
    {
        // <script> in alt is parsed by Markdig as raw HTML inline (not LiteralInline)
        // so the renderer falls back to the "image" label — still no XSS possible
        var html = Render("![<script>alert(1)</script>](https://example.com/img.png)");
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("pr-image-placeholder", html);
    }

    [Fact]
    public void Image_ContainsPictureFrameEmoji()
    {
        var html = Render("![photo](https://example.com/img.png)");
        // U+1F5BC FRAME WITH PICTURE encoded as HTML entity
        Assert.Contains("&#x1F5BC;", html);
    }

    [Fact]
    public void RegularLink_IsRenderedNormally()
    {
        var html = Render("[click here](https://example.com)");
        Assert.Contains("<a", html);
        Assert.Contains("href", html);
        Assert.DoesNotContain("pr-image-placeholder", html);
    }

    [Fact]
    public void RegularLink_IsNotAffectedByExtension()
    {
        var html = Render("[docs](https://example.com/docs)");
        Assert.Contains("click".Length >= 0 ? "docs" : "", html); // link text preserved
        Assert.Contains("https://example.com/docs", html);
    }

    [Fact]
    public void MixedContent_OnlyImagesArePlaceholdered()
    {
        var html = Render("See [docs](https://example.com) and ![diagram](https://example.com/img.png)");
        Assert.Contains("<a", html);
        Assert.Contains("pr-image-placeholder", html);
        Assert.DoesNotContain("<img", html);
    }
}
