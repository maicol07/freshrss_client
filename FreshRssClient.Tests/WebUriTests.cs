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

    [Test]
    [Arguments("https://example.com", true)]
    [Arguments("http://localhost:8080", false)]
    [Arguments("http://10.0.0.4", false)]
    [Arguments("http://192.168.1.4", false)]
    [Arguments("http://feeds.example.com", true)]
    public async Task IsPublicHost_ProtectsPrivateAndLocalHosts(string value, bool expected)
    {
        await Assert.That(WebUri.TryCreate(value, out var uri) && WebUri.IsPublicHost(uri)).IsEqualTo(expected);
    }
}
