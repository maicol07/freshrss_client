using FreshRssClient.Models;
using FreshRssClient.Services;
using TUnit.Assertions;
using TUnit.Core;

namespace FreshRssClient.Tests;

[NotInParallel]
public class FreshRssClientTests
{
    [Test]
    public async Task Localization_Italian_ReturnsItalianStrings()
    {
        LocalizationManager.SetLanguage("it");

        await Assert.That(LocalizationManager.Current.AppTitle).IsEqualTo("Lettore FreshRSS");
        await Assert.That(LocalizationManager.Current.FeedsTab).IsEqualTo("Articoli");
        await Assert.That(LocalizationManager.Current.ShowUnreadOnlyLabel).IsEqualTo("Mostra solo articoli non letti");
    }

    [Test]
    public async Task Localization_English_ReturnsEnglishStrings()
    {
        LocalizationManager.SetLanguage("en");

        await Assert.That(LocalizationManager.Current.AppTitle).IsEqualTo("FreshRSS Client");
        await Assert.That(LocalizationManager.Current.FeedsTab).IsEqualTo("Articles");
        await Assert.That(LocalizationManager.Current.ShowUnreadOnlyLabel).IsEqualTo("Show unread articles only");
    }
}
