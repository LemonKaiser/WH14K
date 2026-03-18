#nullable enable
using System.Collections.Generic;
using Content.Server.Connection;
using Content.Server.Database;
using Content.Shared.Administration;
using NUnit.Framework;

namespace Content.Tests.Server.Connection;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class ConnectionManagerStaffBypassTests
{
    [Test]
    public void HasDiscordAuthBypass_ReturnsTrue_ForModeratorRank()
    {
        var admin = new Admin
        {
            AdminRank = new AdminRank
            {
                Name = "Moderator",
                Flags = new List<AdminRankFlag>
                {
                    new() { Flag = "MODERATOR" },
                },
            },
            Flags = new List<AdminFlag>(),
        };

        Assert.That(ConnectionManagerStaffBypass.HasDiscordAuthBypass(admin), Is.True);
    }

    [Test]
    public void HasDiscordAuthBypass_ReturnsTrue_ForDirectAdminFlag()
    {
        var admin = new Admin
        {
            Flags = new List<AdminFlag>
            {
                new() { Flag = "ADMIN" },
            },
        };

        Assert.That(ConnectionManagerStaffBypass.HasDiscordAuthBypass(admin), Is.True);
    }

    [Test]
    public void HasDiscordAuthBypass_ReturnsFalse_WhenModeratorFlagIsRemoved()
    {
        var admin = new Admin
        {
            AdminRank = new AdminRank
            {
                Name = "Moderator",
                Flags = new List<AdminRankFlag>
                {
                    new() { Flag = "MODERATOR" },
                },
            },
            Flags = new List<AdminFlag>
            {
                new() { Flag = "MODERATOR", Negative = true },
            },
        };

        Assert.That(ConnectionManagerStaffBypass.HasDiscordAuthBypass(admin), Is.False);
    }

    [Test]
    public void HasDiscordAuthBypass_ReturnsFalse_ForSuspendedStaff()
    {
        var admin = new Admin
        {
            Suspended = true,
            Flags = new List<AdminFlag>
            {
                new() { Flag = "ADMIN" },
            },
        };

        Assert.That(ConnectionManagerStaffBypass.HasDiscordAuthBypass(admin), Is.False);
    }

    [Test]
    public void HasDiscordAuthBypass_IgnoresDeadminState_ForStaffRecovery()
    {
        var admin = new Admin
        {
            Deadminned = true,
            Flags = new List<AdminFlag>
            {
                new() { Flag = "MODERATOR" },
            },
        };

        Assert.That(ConnectionManagerStaffBypass.HasDiscordAuthBypass(admin), Is.True);
    }
}
