using Content.Client.Launcher;
using NUnit.Framework;

namespace Content.Tests.Client.Launcher;

[TestFixture]
public sealed class ConnectingAddressParserTests
{
    [Test]
    public void ParsesSs14LocalhostWithTrailingSlash()
    {
        ConnectingAddressParser.ParseAddress("ss14://localhost/", 1212, out var host, out var port);

        Assert.Multiple(() =>
        {
            Assert.That(host, Is.EqualTo("localhost"));
            Assert.That(port, Is.EqualTo((ushort) 1212));
        });
    }

    [Test]
    public void ParsesSs14AddressWithPortAndTrailingSlash()
    {
        ConnectingAddressParser.ParseAddress("ss14://localhost:13000/", 1212, out var host, out var port);

        Assert.Multiple(() =>
        {
            Assert.That(host, Is.EqualTo("localhost"));
            Assert.That(port, Is.EqualTo((ushort) 13000));
        });
    }

    [Test]
    public void IgnoresPathAndQueryAfterAuthority()
    {
        ConnectingAddressParser.ParseAddress("ss14://example.org:14000/some/path?foo=bar", 1212, out var host, out var port);

        Assert.Multiple(() =>
        {
            Assert.That(host, Is.EqualTo("example.org"));
            Assert.That(port, Is.EqualTo((ushort) 14000));
        });
    }
}
