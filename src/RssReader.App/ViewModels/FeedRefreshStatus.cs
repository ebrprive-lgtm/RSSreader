using RssReader.Domain;

namespace RssReader.App.ViewModels;

internal sealed class FeedRefreshStatus
{
    private Dictionary<string, ProfileFeedRefreshState> _states = new(StringComparer.Ordinal);
    private string[] _failedFeedIds = [];
    private string? _failureMessage;
    private bool _isRefreshing;

    public IReadOnlyList<string> FailedFeedIds => _failedFeedIds;
    public string? FailureMessage => _failureMessage;
    public bool IsRefreshing => _isRefreshing;

    public void ReplaceStates(IEnumerable<ProfileFeedRefreshState> states) =>
        _states = states.ToDictionary(state => state.FeedId, StringComparer.Ordinal);

    public void ReplaceFailedFeedIds(IEnumerable<string> feedIds) =>
        _failedFeedIds = feedIds.ToArray();

    public string? GetFailureMessage(string feedId) =>
        _states.TryGetValue(feedId, out var state) ? state.LastFailure : null;

    public string? GetSelectedArticleStatusMessage(string? feedId)
    {
        if (feedId is null || !_states.TryGetValue(feedId, out var state))
        {
            return null;
        }

        var lastAttempt = state.LastAttemptAt.ToLocalTime().ToString("g");
        var lastSuccess = state.LastSuccessfulAt?.ToLocalTime().ToString("g") ?? "Never";
        return $"Last attempt: {lastAttempt}. Last successful refresh: {lastSuccess}.";
    }

    public bool SetFailureMessage(string? message)
    {
        if (string.Equals(_failureMessage, message, StringComparison.Ordinal))
        {
            return false;
        }

        _failureMessage = message;
        return true;
    }

    public bool SetIsRefreshing(bool isRefreshing)
    {
        if (_isRefreshing == isRefreshing)
        {
            return false;
        }

        _isRefreshing = isRefreshing;
        return true;
    }
}
