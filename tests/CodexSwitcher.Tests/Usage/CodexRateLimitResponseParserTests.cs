using System.Text.Json.Nodes;
using CodexSwitcher.Core.Usage;
using CodexSwitcher.Infrastructure.Usage;

namespace CodexSwitcher.Tests.Usage;

[TestClass]
public sealed class CodexRateLimitResponseParserTests
{
    [TestMethod]
    public void Parse_WithRateLimitsByLimitIdCodex_ReturnsWindows()
    {
        var result = Parse(
            """
            {
              "rateLimitsByLimitId": {
                "codex": {
                  "primary": {
                    "usedPercent": 28,
                    "windowDurationMins": 300,
                    "resetsAt": 1782290000
                  },
                  "secondary": {
                    "usedPercent": 59,
                    "windowDurationMins": 10080,
                    "resetsAt": 1782549200
                  }
                }
              }
            }
            """);

        var parsed = CodexRateLimitResponseParser.Parse(result);

        Assert.AreEqual(
            ProfileRateLimitStatus.Available,
            parsed.Status);
        Assert.HasCount(2, parsed.Windows);
        Assert.AreEqual(72, parsed.Windows[0].RemainingPercent);
        Assert.AreEqual(300, parsed.Windows[0].WindowDurationMinutes);
        Assert.AreEqual(41, parsed.Windows[1].RemainingPercent);
        Assert.AreEqual(10080, parsed.Windows[1].WindowDurationMinutes);
    }

    [TestMethod]
    public void Parse_WithLegacyRateLimits_ReturnsWindows()
    {
        var result = Parse(
            """
            {
              "rateLimits": {
                "primary": {
                  "usedPercent": 5,
                  "windowDurationMins": 300
                }
              }
            }
            """);

        var parsed = CodexRateLimitResponseParser.Parse(result);

        Assert.AreEqual(
            ProfileRateLimitStatus.Available,
            parsed.Status);
        Assert.HasCount(1, parsed.Windows);
        Assert.AreEqual(95, parsed.Windows[0].RemainingPercent);
        Assert.AreEqual(300, parsed.Windows[0].WindowDurationMinutes);
    }

    [TestMethod]
    public void Parse_WithoutRateLimitWindows_ReturnsChangedResponse()
    {
        var result = Parse(
            """
            {
              "rateLimitsByLimitId": {
                "codex": {
                  "limits": []
                }
              }
            }
            """);

        var parsed = CodexRateLimitResponseParser.Parse(result);

        Assert.AreEqual(
            ProfileRateLimitStatus.ResponseFormatChanged,
            parsed.Status);
        Assert.IsEmpty(parsed.Windows);
    }

    private static JsonObject Parse(string json) =>
        JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidOperationException(
            "테스트 JSON을 읽을 수 없습니다.");
}
