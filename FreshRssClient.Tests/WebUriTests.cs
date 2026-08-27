using FreshRssClient.Helpers;
using TUnit.Assertions;
using TUnit.Core;

namespace FreshRssClient.Tests;

public class WebUriTests
{
    [Test]
    [Arguments("https://example.com", true)]
    [Arguments("http://localhost:8080/article", true)]
    [Arguments("file:///C:/secret.txt", false)]
    [Arguments("custom:payload", false)]
    public async Task TryCreate_Scheme_ReturnsExpectedResult(string value, bool expected)
    {
        await Assert.That(WebUri.TryCreate(value, out _)).IsEqualTo(expected);
    }
}
