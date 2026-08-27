using System;
using FreshRssClient.Services;
using TUnit.Assertions;
using TUnit.Core;

namespace FreshRssClient.Tests
{
    public class ArticleImageResolverTests
    {
        private static readonly Uri PageUri = new("https://example.com/news/article-1");

        [Test]
        public async Task ExtractSocialImage_PrefersOpenGraphSecureUrl()
        {
            const string html = """
                <html><head>
                <meta name="twitter:image" content="https://cdn.example.com/twitter.jpg">
                <meta property="og:image" content="https://cdn.example.com/og.jpg">
                <meta property="og:image:secure_url" content="https://cdn.example.com/secure.jpg">
                </head></html>
                """;

            var result = ArticleImageResolver.ExtractSocialImage(html, PageUri);

            await Assert.That(result).IsEqualTo("https://cdn.example.com/secure.jpg");
        }

        [Test]
        public async Task ExtractSocialImage_FallsBackToTwitterImage()
        {
            const string html = """<meta content="https://cdn.example.com/twitter.jpg" name="twitter:image"/>""";

            var result = ArticleImageResolver.ExtractSocialImage(html, PageUri);

            await Assert.That(result).IsEqualTo("https://cdn.example.com/twitter.jpg");
        }

        [Test]
        public async Task ExtractSocialImage_SupportsLinkRelImageSrc()
        {
            const string html = """<link rel="image_src" href="/assets/cover.png">""";

            var result = ArticleImageResolver.ExtractSocialImage(html, PageUri);

            await Assert.That(result).IsEqualTo("https://example.com/assets/cover.png");
        }

        [Test]
        public async Task ExtractSocialImage_ResolvesRelativeUrlsAndDecodesEntities()
        {
            const string html = """<meta property="og:image" content="../media/cover.jpg?w=800&amp;h=600">""";

            var result = ArticleImageResolver.ExtractSocialImage(html, PageUri);

            await Assert.That(result).IsEqualTo("https://example.com/media/cover.jpg?w=800&h=600");
        }

        [Test]
        [Arguments("""<meta property="og:image" content="">""")]
        [Arguments("""<meta property="og:image" content="javascript:alert(1)">""")]
        [Arguments("""<meta property="og:title" content="No image here">""")]
        [Arguments("<html><head><title>Nothing</title></head></html>")]
        [Arguments("")]
        public async Task ExtractSocialImage_ReturnsNullWhenNoUsableImage(string html)
        {
            var result = ArticleImageResolver.ExtractSocialImage(html, PageUri);

            await Assert.That(result).IsNull();
        }

        [Test]
        public async Task ResolveAsync_SkipsPrivateAndInvalidHosts()
        {
            var resolver = new ArticleImageResolver();

            await Assert.That(await resolver.ResolveAsync("http://192.168.1.10/article")).IsNull();
            await Assert.That(await resolver.ResolveAsync("file:///C:/article.html")).IsNull();
            await Assert.That(await resolver.ResolveAsync(null)).IsNull();
        }
    }
}
