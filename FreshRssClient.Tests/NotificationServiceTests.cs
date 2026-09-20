using System;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;
using FreshRssClient.Models;
using FreshRssClient.Services;

namespace FreshRssClient.Tests
{
    [NotInParallel]
    public class NotificationServiceTests
    {
        [Test]
        public async Task TestNotificationService_SendArticleNotification_ExecutesWithoutThrowing()
        {
            // Arrange
            var notificationService = new NotificationService();

            // Set language to English so localized string templates are loaded
            LocalizationManager.SetLanguage("en");

            // Act & Assert
            // We verify that executing the notification service in a unit test runner environment
            // does not throw any exceptions (it safely handles ToastNotificationManager environment failures internally).
            
            // 1. Standard execution
            notificationService.SendArticleNotification("art1", "Tech Feed", "Exciting new article is out!", "https://example.com/image.png");
 
            // 2. Execution with null image URL
            notificationService.SendArticleNotification("art2", "Tech Feed", "Exciting new article is out!", null);
 
            // 3. Execution with invalid and dangerous XML characters (ensures XML escaping code is safe)
            notificationService.SendArticleNotification("art3", "XML <Dangerous> & Feed", "Body containing \"quotes\" and 'apostrophes' & <tags>", "invalid-image-url-string");
        }

        [Test]
        public async Task TestNotificationService_UpdateBadge_ExecutesWithoutThrowing()
        {
            // Arrange
            var notificationService = new NotificationService();

            // Act & Assert
            // 1. Positive unread count
            notificationService.UpdateBadge(12);

            // 2. Zero count (should clear the badge)
            notificationService.UpdateBadge(0);

            // 3. Negative count (should clear the badge)
            notificationService.UpdateBadge(-5);
        }

        [Test]
        public async Task TestNotificationService_BuildToastXml_WithImageUrl_IncludesHeroImage()
        {
            LocalizationManager.SetLanguage("en");
            var xml = NotificationService.BuildToastXml("art1", "Tech", "Big News", "https://example.com/banner.jpg");

            await Assert.That(xml).Contains("<image placement='hero' src='https://example.com/banner.jpg'/>");
            await Assert.That(xml).Contains("launch='articleId=art1'");
            await Assert.That(xml).Contains("<text>New article from Tech</text>");
            await Assert.That(xml).Contains("<text>Big News</text>");

            // Verify XML is well-formed
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            await Assert.That(doc.Root).IsNotNull();
        }

        [Test]
        public async Task TestNotificationService_BuildToastXml_WithoutImageUrl_OmitsImageNode()
        {
            LocalizationManager.SetLanguage("en");
            var xml = NotificationService.BuildToastXml("art2", "Tech", "Text Only", null);

            await Assert.That(xml).DoesNotContain("<image");
            await Assert.That(xml).Contains("<text>Text Only</text>");

            var doc = System.Xml.Linq.XDocument.Parse(xml);
            await Assert.That(doc.Root).IsNotNull();
        }

        [Test]
        public async Task TestNotificationService_BuildToastXml_EscapesSpecialCharacters()
        {
            LocalizationManager.SetLanguage("en");
            var xml = NotificationService.BuildToastXml("art&3", "Tech & Co <Review>", "Quotes \"and\" 'apostrophes'", "https://example.com/image?a=1&b=2");

            await Assert.That(xml).Contains("launch='articleId=art&amp;3'");
            await Assert.That(xml).Contains("Tech &amp; Co &lt;Review&gt;");
            await Assert.That(xml).Contains("Quotes &quot;and&quot; &apos;apostrophes&apos;");
            await Assert.That(xml).Contains("src='https://example.com/image?a=1&amp;b=2'");

            var doc = System.Xml.Linq.XDocument.Parse(xml);
            await Assert.That(doc.Root).IsNotNull();
        }
    }
}
