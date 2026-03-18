using System;

namespace Content.Client.Launcher;

public static class ConnectingAddressParser
{
    public static void ParseAddress(string address, ushort defaultPort, out string host, out ushort port)
    {
        var trimmed = address.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Address is empty.");

        var schemeSeparator = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0)
            trimmed = trimmed[(schemeSeparator + 3)..];

        var authorityEnd = trimmed.IndexOfAny(['/', '?', '#']);
        if (authorityEnd >= 0)
            trimmed = trimmed[..authorityEnd];

        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Address is empty.");

        if (trimmed.StartsWith('['))
        {
            var closingBracket = trimmed.IndexOf(']');
            if (closingBracket <= 1)
                throw new ArgumentException("Not a valid Address.");

            host = trimmed[1..closingBracket];
            port = defaultPort;

            if (closingBracket == trimmed.Length - 1)
                return;

            if (trimmed[closingBracket + 1] != ':')
                throw new ArgumentException("Not a valid Address.");

            var portText = trimmed[(closingBracket + 2)..];
            if (!ushort.TryParse(portText, out port))
                throw new ArgumentException("Not a valid port.");

            return;
        }

        var split = trimmed.Split(':');
        if (split.Length > 2)
            throw new ArgumentException("Not a valid Address.");

        host = trimmed;
        port = defaultPort;

        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Address is empty.");

        if (split.Length != 2)
            return;

        host = split[0];
        if (!ushort.TryParse(split[1], out port))
            throw new ArgumentException("Not a valid port.");
    }
}
