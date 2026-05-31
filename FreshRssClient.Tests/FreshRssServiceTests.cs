using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;
using FreshRssClient.Services;

namespace FreshRssClient.Tests
{
    [NotInParallel]
    public class FreshRssServiceTests
    {
        [Test]
        public async Task TestAuthenticateAsync_Success_ReturnsTrueAndSetsToken()
        {
            // Arrange
            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new AdvancedMockHttpMessageHandler
            {
                HandlerFunc = req =>
                {
                    capturedRequest = req;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("SID=something\nLSID=something\nAuth=abc_session_token_123\n")
                    };
                }
            };

            var service = new FreshRssService(null, new HttpClient(mockHandler));

            // Act
            var result = await service.AuthenticateAsync("https://example.com", "my_user", "my_api_password");

            // Assert
            await Assert.That(result).IsTrue();
            await Assert.That(service.LastConnectionFailed).IsFalse();
            await Assert.That(capturedRequest).IsNotNull();
            await Assert.That(capturedRequest!.RequestUri!.ToString()).Contains("/accounts/ClientLogin");
        }

        [Test]
        public async Task TestAuthenticateAsync_Failure_ReturnsFalse()
        {
            // Arrange
            var mockHandler = new AdvancedMockHttpMessageHandler
            {
                HandlerFunc = req => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            };

            var service = new FreshRssService(null, new HttpClient(mockHandler));

            // Act
            var result = await service.AuthenticateAsync("https://example.com", "my_user", "wrong_password");

            // Assert
            await Assert.That(result).IsFalse();
            await Assert.That(service.LastConnectionFailed).IsFalse();
        }

        [Test]
        public async Task TestAuthenticateAsync_Exception_SetsLastConnectionFailed()
        {
            // Arrange
            var mockHandler = new AdvancedMockHttpMessageHandler
            {
                HandlerFunc = req => throw new HttpRequestException("Network down")
            };

            var service = new FreshRssService(null, new HttpClient(mockHandler));

            // Act
            var result = await service.AuthenticateAsync("https://example.com", "my_user", "my_api_password");

            // Assert
            await Assert.That(result).IsFalse();
            await Assert.That(service.LastConnectionFailed).IsTrue();
        }

        [Test]
        public async Task TestFetchArticlesAsync_Unauthenticated_ReturnsEmptyList()
        {
            // Arrange
            var service = new FreshRssService();

            // Act
            var articles = await service.FetchArticlesAsync(null, false, 50, false);

            // Assert
            await Assert.That(articles).IsEmpty();
        }

        [Test]
        public async Task TestFetchArticlesAsync_Success_ParsesJsonAndExtractsImages()
        {
            // Arrange
            var mockHandler = new AdvancedMockHttpMessageHandler();
            var service = new FreshRssService(null, new HttpClient(mockHandler));

            // Perform authenticating so we can fetch
            mockHandler.HandlerFunc = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Auth=token")
            };
            await service.AuthenticateAsync("https://example.com", "user", "pass");

            // Configure Mock response for Articles
            var articlesJson = @"
            {
              ""id"": ""reading-list"",
              ""items"": [
                {
                  ""id"": ""item1"",
                  ""title"": ""Article 1"",
                  ""published"": 1716900000,
                  ""alternate"": [{""href"": ""https://example.com/art1""}],
                  ""summary"": { ""content"": ""Summary body <b>bold text</b>"" },
                  ""origin"": { ""streamId"": ""feed1"", ""title"": ""My Feed"", ""htmlUrl"": ""https://example.com"" },
                  ""categories"": [""user/-/state/com.google/reading-list""]
                },
                {
                  ""id"": ""item2"",
                  ""title"": ""Article 2"",
                  ""published"": 1716800000,
                  ""alternate"": [{""href"": ""https://example.com/art2""}],
                  ""content"": { ""content"": ""Body text with <img src='https://example.com/my-pic.jpg' /> image."" },
                  ""origin"": { ""streamId"": ""feed1"", ""title"": ""My Feed"", ""htmlUrl"": ""https://example.com"" },
                  ""categories"": [""user/-/state/com.google/read""]
                }
              ]
            }";

            HttpRequestMessage? capturedRequest = null;
            mockHandler.HandlerFunc = req =>
            {
                capturedRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(articlesJson, System.Text.Encoding.UTF8, "application/json")
                };
            };

            // Act
            var articles = await service.FetchArticlesAsync(null, false, 50, false);

            // Assert
            await Assert.That(capturedRequest).IsNotNull();
            await Assert.That(capturedRequest!.RequestUri!.ToString()).Contains("/reader/api/0/stream/contents/");
            
            await Assert.That(articles).Count().IsEqualTo(2);
            
            var art1 = articles[0]; // Newest should be first due to sort (published: 1716900000)
            await Assert.That(art1.Id).IsEqualTo("item1");
            await Assert.That(art1.Title).IsEqualTo("Article 1");
            await Assert.That(art1.Summary).IsEqualTo("Summary body bold text"); // HTML tags should be stripped
            await Assert.That(art1.ImageUrl).IsNull();
            await Assert.That(art1.IsRead).IsFalse();

            var art2 = articles[1];
            await Assert.That(art2.Id).IsEqualTo("item2");
            await Assert.That(art2.ImageUrl).IsEqualTo("https://example.com/my-pic.jpg"); // Image should be extracted
            await Assert.That(art2.IsRead).IsTrue();
        }

        [Test]
        public async Task TestFetchArticlesAsync_ShowUnreadOnly_FiltersReadArticles()
        {
            // Arrange
            var mockHandler = new AdvancedMockHttpMessageHandler();
            var service = new FreshRssService(null, new HttpClient(mockHandler));

            mockHandler.HandlerFunc = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Auth=token")
            };
            await service.AuthenticateAsync("https://example.com", "user", "pass");

            var articlesJson = @"
            {
              ""items"": [
                {
                  ""id"": ""item1"",
                  ""title"": ""Article 1"",
                  ""published"": 1716900000,
                  ""categories"": [""user/-/state/com.google/reading-list""]
                },
                {
                  ""id"": ""item2"",
                  ""title"": ""Article 2"",
                  ""published"": 1716800000,
                  ""categories"": [""user/-/state/com.google/read""]
                }
              ]
            }";

            mockHandler.HandlerFunc = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(articlesJson, System.Text.Encoding.UTF8, "application/json")
            };

            // Act - Fetch with showUnreadOnly = true
            var articles = await service.FetchArticlesAsync(null, true, 50, false);

            // Assert
            await Assert.That(articles).Count().IsEqualTo(1);
            await Assert.That(articles[0].Id).IsEqualTo("item1");
        }

        [Test]
        public async Task TestFetchArticlesAsync_MaxReadArticles_LimitsReadArticles()
        {
            // Arrange
            var mockHandler = new AdvancedMockHttpMessageHandler();
            var service = new FreshRssService(null, new HttpClient(mockHandler));

            mockHandler.HandlerFunc = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Auth=token")
            };
            await service.AuthenticateAsync("https://example.com", "user", "pass");

            var articlesJson = @"
            {
              ""items"": [
                {
                  ""id"": ""item1"",
                  ""title"": ""Article 1"",
                  ""published"": 1716900000,
                  ""categories"": [""user/-/state/com.google/read""]
                },
                {
                  ""id"": ""item2"",
                  ""title"": ""Article 2"",
                  ""published"": 1716800000,
                  ""categories"": [""user/-/state/com.google/read""]
                }
              ]
            }";

            mockHandler.HandlerFunc = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(articlesJson, System.Text.Encoding.UTF8, "application/json")
            };

            // Act - Fetch with maxReadArticles = 1
            var articles = await service.FetchArticlesAsync(null, false, 1, false);

            // Assert
            await Assert.That(articles).Count().IsEqualTo(1);
            await Assert.That(articles[0].Id).IsEqualTo("item1");
        }

        [Test]
        public async Task TestMarkAsReadAsync_Success_ReturnsTrue()
        {
            // Arrange
            var mockHandler = new AdvancedMockHttpMessageHandler();
            var service = new FreshRssService(null, new HttpClient(mockHandler));

            mockHandler.HandlerFunc = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Auth=token")
            };
            await service.AuthenticateAsync("https://example.com", "user", "pass");

            HttpRequestMessage? capturedRequest = null;
            mockHandler.HandlerFunc = req =>
            {
                capturedRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK);
            };

            // Act
            var result = await service.MarkAsReadAsync("article_123");

            // Assert
            await Assert.That(result).IsTrue();
            await Assert.That(capturedRequest).IsNotNull();
            await Assert.That(capturedRequest!.Method).IsEqualTo(HttpMethod.Post);
            await Assert.That(capturedRequest!.RequestUri!.ToString()).Contains("/reader/api/0/edit-tag");
        }

        [Test]
        public async Task TestFetchSubscriptionsAndUnreadCountsAsync_Success_GroupsFeedsAndHandlesUncategorized()
        {
            // Arrange
            var mockHandler = new AdvancedMockHttpMessageHandler();
            var service = new FreshRssService(null, new HttpClient(mockHandler));

            mockHandler.HandlerFunc = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Auth=token")
            };
            await service.AuthenticateAsync("https://example.com", "user", "pass");

            // Mock responses for subscription/list and unread-count
            var subsJson = @"
            {
              ""subscriptions"": [
                {
                  ""id"": ""feed/1"",
                  ""title"": ""Tech News"",
                  ""htmlUrl"": ""https://tech.example.com"",
                  ""categories"": [
                    { ""id"": ""user/-/label/Tech"", ""label"": ""Tech"" }
                  ]
                },
                {
                  ""id"": ""feed/2"",
                  ""title"": ""General News"",
                  ""htmlUrl"": ""https://general.example.com"",
                  ""categories"": []
                }
              ]
            }";

            var unreadJson = @"
            {
              ""unreadcounts"": [
                { ""id"": ""feed/1"", ""count"": 3 },
                { ""id"": ""feed/2"", ""count"": 5 },
                { ""id"": ""user/-/label/Tech"", ""count"": 3 }
              ]
            }";

            mockHandler.HandlerFunc = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("/subscription/list"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(subsJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }
                else if (uri.Contains("/unread-count"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(unreadJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };

            // Act
            var (categories, feeds) = await service.FetchSubscriptionsAndUnreadCountsAsync();

            // Assert
            await Assert.That(feeds).Count().IsEqualTo(2);
            await Assert.That(feeds[0].Id).IsEqualTo("feed/1");
            await Assert.That(feeds[0].UnreadCount).IsEqualTo(3);
            await Assert.That(feeds[1].Id).IsEqualTo("feed/2");
            await Assert.That(feeds[1].UnreadCount).IsEqualTo(5);

            // Tech category + Uncategorized group
            await Assert.That(categories).Count().IsEqualTo(2);

            var techCat = categories[0];
            await Assert.That(techCat.Id).IsEqualTo("user/-/label/Tech");
            await Assert.That(techCat.Title).IsEqualTo("Tech");
            await Assert.That(techCat.UnreadCount).IsEqualTo(3);
            await Assert.That(techCat.Feeds).Count().IsEqualTo(1);
            await Assert.That(techCat.Feeds[0].Id).IsEqualTo("feed/1");

            var uncategorizedCat = categories[1];
            await Assert.That(uncategorizedCat.Id).IsEqualTo("uncategorized");
            await Assert.That(uncategorizedCat.UnreadCount).IsEqualTo(5);
            await Assert.That(uncategorizedCat.Feeds).Count().IsEqualTo(1);
            await Assert.That(uncategorizedCat.Feeds[0].Id).IsEqualTo("feed/2");
        }
    }

    public class AdvancedMockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> HandlerFunc { get; set; } = req => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(HandlerFunc(request));
        }
    }
}
