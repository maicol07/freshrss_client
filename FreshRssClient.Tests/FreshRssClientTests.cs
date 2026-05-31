using System;
using System.Net.Http;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;
using FreshRssClient.Services;

namespace FreshRssClient.Tests
{
    [NotInParallel]
    public class FreshRssClientTests
    {
        [Test]
        public async Task TestLocalization_Italian_ReturnsItalianStrings()
        {
            // Act
            LocalizationManager.SetLanguage("it");
            var title = LocalizationManager.Current.AppTitle;
            var feedsTab = LocalizationManager.Current.FeedsTab;
            var showUnreadOnly = LocalizationManager.Current.ShowUnreadOnlyLabel;

            // Assert
            await Assert.That(title).IsEqualTo("Lettore FreshRSS");
            await Assert.That(feedsTab).IsEqualTo("Articoli");
            await Assert.That(showUnreadOnly).IsEqualTo("Mostra solo articoli non letti");
        }

        [Test]
        public async Task TestLocalization_English_ReturnsEnglishStrings()
        {
            // Act
            LocalizationManager.SetLanguage("en");
            var title = LocalizationManager.Current.AppTitle;
            var feedsTab = LocalizationManager.Current.FeedsTab;
            var showUnreadOnly = LocalizationManager.Current.ShowUnreadOnlyLabel;

            // Assert
            await Assert.That(title).IsEqualTo("FreshRSS Client");
            await Assert.That(feedsTab).IsEqualTo("Articles");
            await Assert.That(showUnreadOnly).IsEqualTo("Show unread articles only");
        }

        [Test]
        public async Task TestOpenGraphService_ExtractsImageAndDescription()
        {
            // Arrange
            // We use a custom HttpClient with a mock message handler for local offline testing
            var mockHandler = new MockHttpMessageHandler(
                "<html><head>" +
                "<meta property=\"og:image\" content=\"https://example.com/cover.jpg\" />" +
                "<meta property=\"og:description\" content=\"This is a test description from OpenGraph metadata.\" />" +
                "</head></html>"
            );
            var httpClient = new HttpClient(mockHandler);
            var ogService = new OpenGraphService(httpClient);

            // Act
            var (description, imageUrl) = await ogService.FetchOpenGraphMetadataAsync("https://example.com/article1");

            // Assert
            await Assert.That(imageUrl).IsEqualTo("https://example.com/cover.jpg");
            await Assert.That(description).IsEqualTo("This is a test description from OpenGraph metadata.");
        }

        [Test]
        public async Task TestOpenGraphService_ExtractsTwitterImageAndStandardDescription()
        {
            // Arrange
            var mockHandler = new MockHttpMessageHandler(
                "<html><head>" +
                "<meta name=\"twitter:image\" content=\"https://example.com/twitter-card.png\" />" +
                "<meta name=\"description\" content=\"A standard SEO HTML description tag value.\" />" +
                "</head></html>"
            );
            var httpClient = new HttpClient(mockHandler);
            var ogService = new OpenGraphService(httpClient);

            // Act
            var (description, imageUrl) = await ogService.FetchOpenGraphMetadataAsync("https://example.com/article2");

            // Assert
            await Assert.That(imageUrl).IsEqualTo("https://example.com/twitter-card.png");
            await Assert.That(description).IsEqualTo("A standard SEO HTML description tag value.");
        }

        [Test]
        public async Task TestOpenGraphService_HandlesInvalidUrlGracefully()
        {
            // Arrange
            var ogService = new OpenGraphService();

            // Act
            var (description, imageUrl) = await ogService.FetchOpenGraphMetadataAsync("invalid-url-string");

            // Assert
            await Assert.That(description).IsNull();
            await Assert.That(imageUrl).IsNull();
        }

        [Test]
        public async Task TestOpenGraphService_HandlesHttpErrorGracefully()
        {
            // Arrange
            var mockHandler = new MockHttpMessageHandler(string.Empty, System.Net.HttpStatusCode.NotFound);
            var httpClient = new HttpClient(mockHandler);
            var ogService = new OpenGraphService(httpClient);

            // Act
            var (description, imageUrl) = await ogService.FetchOpenGraphMetadataAsync("https://example.com/404page");

            // Assert
            await Assert.That(description).IsNull();
            await Assert.That(imageUrl).IsNull();
        }
    }

    // Mock Http Message Handler for offline unit testing
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        private readonly System.Net.HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseContent, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "text/html")
            };
            return Task.FromResult(response);
        }
    }
}
