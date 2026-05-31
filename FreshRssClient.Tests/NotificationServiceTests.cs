using System;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;
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
    }
}
