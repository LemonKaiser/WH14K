using System.Runtime.InteropServices;
using System.Text;

namespace Content.Server._WH40K.Chat.Moderation;

internal sealed class WH40KRepeatedChatSpamTracker
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public WH40KRepeatedChatSpamResult CountMessage(
        TimeSpan now,
        string normalizedMessage,
        TimeSpan period,
        int triggerCount,
        TimeSpan? adminAnnounceDelay)
    {
        if (triggerCount <= 0 || period <= TimeSpan.Zero || string.IsNullOrWhiteSpace(normalizedMessage))
            return WH40KRepeatedChatSpamResult.Allowed;

        CleanupExpired(now);

        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_entries, normalizedMessage, out var exists);
        if (!exists || entry.ExpiresAt <= now)
        {
            entry = new Entry
            {
                ExpiresAt = now + period,
            };
        }

        entry.Count++;

        if (entry.Count < triggerCount)
            return new WH40KRepeatedChatSpamResult(false, false, false, entry.Count);

        var shouldAnnounceAdmins = false;
        if (adminAnnounceDelay is { TotalSeconds: >= 0 } delay && entry.NextAdminAnnounce <= now)
        {
            shouldAnnounceAdmins = true;
            entry.NextAdminAnnounce = now + delay;
        }

        var firstViolation = !entry.Announced;
        entry.Announced = true;

        return new WH40KRepeatedChatSpamResult(true, firstViolation, shouldAnnounceAdmins, entry.Count);
    }

    public bool CleanupExpired(TimeSpan now)
    {
        if (_entries.Count == 0)
            return true;

        List<string>? expired = null;
        foreach (var (message, entry) in _entries)
        {
            if (entry.ExpiresAt > now)
                continue;

            expired ??= new List<string>();
            expired.Add(message);
        }

        if (expired != null)
        {
            foreach (var key in expired)
            {
                _entries.Remove(key);
            }
        }

        return _entries.Count == 0;
    }

    public static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var trimmed = message.Trim();
        var builder = new StringBuilder(trimmed.Length);
        var pendingSpace = false;

        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private struct Entry
    {
        public TimeSpan ExpiresAt;
        public int Count;
        public bool Announced;
        public TimeSpan NextAdminAnnounce;
    }
}

internal readonly record struct WH40KRepeatedChatSpamResult(
    bool Blocked,
    bool FirstViolation,
    bool ShouldAnnounceAdmins,
    int Count)
{
    public static readonly WH40KRepeatedChatSpamResult Allowed = new(false, false, false, 0);
}
