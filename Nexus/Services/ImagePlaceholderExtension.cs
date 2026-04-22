using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace Nexus.Services;

public static class ImagePlaceholderExtension
{
    public static MarkdownPipelineBuilder UseImagePlaceholders(this MarkdownPipelineBuilder builder) =>
        builder.Use<ImagePlaceholderMarkdigExtension>();

    private class ImagePlaceholderMarkdigExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline) { }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            if (renderer is not HtmlRenderer html) return;
            html.ObjectRenderers.ReplaceOrAdd<LinkInlineRenderer>(new ImagePlaceholderRenderer());
        }
    }

    private class ImagePlaceholderRenderer : LinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, LinkInline link)
        {
            if (!link.IsImage)
            {
                base.Write(renderer, link);
                return;
            }

            var alt = link.FirstChild is LiteralInline lit ? lit.Content.ToString() : null;
            var label = string.IsNullOrWhiteSpace(alt) ? "image" : alt;
            renderer.Write($"<span class=\"pr-image-placeholder\">&#x1F5BC; {System.Net.WebUtility.HtmlEncode(label)}</span>");
        }
    }
}
