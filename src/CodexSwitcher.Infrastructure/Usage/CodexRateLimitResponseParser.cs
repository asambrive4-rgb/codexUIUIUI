using System.Text.Json.Nodes;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Infrastructure.Usage;

internal static class CodexRateLimitResponseParser
{
    public static ProfileRateLimitReadResult Parse(JsonObject result)
    {
        if (!TrySelectCodexSnapshot(result, out var snapshot))
        {
            return ChangedResponse();
        }

        var windows = ReadWindows(snapshot);
        return windows.Count == 0
            ? ChangedResponse()
            : new ProfileRateLimitReadResult(
                ProfileRateLimitStatus.Available,
                windows);
    }

    private static bool TrySelectCodexSnapshot(
        JsonObject result,
        out JsonObject snapshot)
    {
        var byLimitId = result["rateLimitsByLimitId"] as JsonObject;
        if (byLimitId?["codex"] is JsonObject codex)
        {
            snapshot = codex;
            return true;
        }

        if (result["rateLimits"] is JsonObject legacy)
        {
            snapshot = legacy;
            return true;
        }

        snapshot = [];
        return false;
    }

    private static IReadOnlyList<RateLimitWindow> ReadWindows(
        JsonObject snapshot)
    {
        var windows = new List<RateLimitWindow>(2);
        AddWindow(snapshot["primary"], windows);
        AddWindow(snapshot["secondary"], windows);
        return windows;
    }

    private static void AddWindow(
        JsonNode? node,
        ICollection<RateLimitWindow> destination)
    {
        if (node is not JsonObject window ||
            window["usedPercent"] is null)
        {
            return;
        }

        var resetsAt = window["resetsAt"]?.GetValue<long?>();
        destination.Add(
            new RateLimitWindow(
                window["usedPercent"]!.GetValue<int>(),
                window["windowDurationMins"]?.GetValue<long?>(),
                resetsAt is null
                    ? null
                    : DateTimeOffset.FromUnixTimeSeconds(
                        resetsAt.Value)));
    }

    private static ProfileRateLimitReadResult ChangedResponse() =>
        new(ProfileRateLimitStatus.ResponseFormatChanged, []);
}
