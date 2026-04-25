using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Client.Launcher;

public readonly record struct ConnectionFallbackTarget(string Address, string Host, ushort Port)
{
    public string Key => $"{Host.Trim().ToLowerInvariant()}:{Port}";
}

public static class ConnectionFallbackHelper
{
    private static readonly char[] AddressSeparators = [',', ';', '\n', '\r'];

    private static readonly string[] TransportFailureKeywords =
    [
        "timed out",
        "timeout",
        "no response",
        "failed to establish",
        "actively refused",
        "connection refused",
        "host unreachable",
        "network unreachable",
        "unreachable",
        "connection attempt failed",
        "socket",
        "reset by peer",
        "remote host",
        "transport",
        "unable to resolve domain",
        "has no associated ip addresses",
        "dns"
    ];

    public static List<string> SplitAddressList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(AddressSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .ToList();
    }

    public static bool TryPickAlternative(
        string? currentAddress,
        string primaryAddresses,
        string alternateAddresses,
        ushort defaultPort,
        IReadOnlySet<string>? skippedTargetKeys,
        out ConnectionFallbackTarget target)
    {
        target = default;

        if (!TryParseTarget(currentAddress, defaultPort, out var current))
            return false;

        var primaryList = SplitAddressList(primaryAddresses);
        if (primaryList.Count > 0 &&
            !primaryList.Any(primary => IsSameEndpoint(primary, current, defaultPort)))
        {
            return false;
        }

        foreach (var address in SplitAddressList(alternateAddresses))
        {
            if (!TryParseTarget(address, defaultPort, out var candidate))
                continue;

            if (string.Equals(candidate.Key, current.Key, StringComparison.OrdinalIgnoreCase))
                continue;

            if (skippedTargetKeys?.Contains(candidate.Key) == true)
                continue;

            target = candidate;
            return true;
        }

        return false;
    }

    public static bool IsNetworkFallbackEligible(string? reason, bool redial)
    {
        if (redial || string.IsNullOrWhiteSpace(reason))
            return false;

        var text = reason.Trim();

        if (StartsWithOrdinalIgnoreCase(text, "unable to resolve domain"))
            return true;

        if (StartsWithOrdinalIgnoreCase(text, "domain ") &&
            ContainsOrdinalIgnoreCase(text, " has no associated ip addresses"))
        {
            return true;
        }

        if (StartsWithOrdinalIgnoreCase(text, "connection failed:"))
        {
            var detail = text["connection failed:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(detail))
                return true;

            if (StartsWithOrdinalIgnoreCase(detail, "disconnected:"))
                return false;

            return ContainsTransportFailureKeyword(detail);
        }

        return ContainsTransportFailureKeyword(text);
    }

    public static bool TryParseTarget(string? address, ushort defaultPort, out ConnectionFallbackTarget target)
    {
        target = default;

        if (string.IsNullOrWhiteSpace(address))
            return false;

        try
        {
            ConnectingAddressParser.ParseAddress(address, defaultPort, out var host, out var port);
            target = new ConnectionFallbackTarget(address.Trim(), host, port);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSameEndpoint(string address, ConnectionFallbackTarget target, ushort defaultPort)
    {
        return TryParseTarget(address, defaultPort, out var parsed) &&
               string.Equals(parsed.Key, target.Key, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTransportFailureKeyword(string text)
    {
        return TransportFailureKeywords.Any(keyword => ContainsOrdinalIgnoreCase(text, keyword));
    }

    private static bool StartsWithOrdinalIgnoreCase(string text, string value)
    {
        return text.StartsWith(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOrdinalIgnoreCase(string text, string value)
    {
        return text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
