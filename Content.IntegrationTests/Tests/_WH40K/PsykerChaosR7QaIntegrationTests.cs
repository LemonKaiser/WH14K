#nullable enable
using System;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class PsykerChaosR7QaIntegrationTests
{
    [Test]
    public void R7FirstUseSelectorLocksAfterInitialAttunement()
    {
        var progression = new WH40KChaosGiftProgressionComponent();

        Assert.That(ShouldOpenPatronSelector(progression), Is.True,
            "Fresh chaos progression must open patron selector on first skrizhal use.");

        var result = TrySelectPatron(progression, WH40KChaosPatron.Khorne);
        Assert.That(result, Is.EqualTo(PatronSelectionOutcome.Success));

        Assert.Multiple(() =>
        {
            Assert.That(progression.AttunedPatron, Is.EqualTo(WH40KChaosPatron.Khorne));
            Assert.That(progression.PatronSelectionLocked, Is.True);
            Assert.That(ShouldOpenPatronSelector(progression), Is.False,
                "After first attunement, selector must stay closed in normal flow.");
        });
    }

    [Test]
    public void R7OneTimePatronBindBlocksRebindWhenSwitchDisabled()
    {
        var progression = new WH40KChaosGiftProgressionComponent
        {
            AttunedPatron = WH40KChaosPatron.Nurgle,
            PatronSelectionLocked = true,
            AllowPatronSwitch = false,
            PrimaryGiftSlot = 2,
            GiftSlotOneUnlocked = true,
            GiftSlotTwoUnlocked = true,
            GiftSlotThreeUnlocked = true,
        };

        var sameResult = TrySelectPatron(progression, WH40KChaosPatron.Nurgle);
        var switchResultBlocked = TrySelectPatron(progression, WH40KChaosPatron.Tzeentch);

        Assert.Multiple(() =>
        {
            Assert.That(sameResult, Is.EqualTo(PatronSelectionOutcome.AlreadyAttuned));
            Assert.That(switchResultBlocked, Is.EqualTo(PatronSelectionOutcome.SwitchBlocked));
            Assert.That(progression.AttunedPatron, Is.EqualTo(WH40KChaosPatron.Nurgle));
            Assert.That(progression.PrimaryGiftSlot, Is.EqualTo(2));
            Assert.That(progression.GiftSlotOneUnlocked, Is.True);
            Assert.That(progression.GiftSlotTwoUnlocked, Is.True);
            Assert.That(progression.GiftSlotThreeUnlocked, Is.True);
        });

        progression.AllowPatronSwitch = true;
        var switchResultAllowed = TrySelectPatron(progression, WH40KChaosPatron.Tzeentch);

        Assert.Multiple(() =>
        {
            Assert.That(switchResultAllowed, Is.EqualTo(PatronSelectionOutcome.Success));
            Assert.That(progression.AttunedPatron, Is.EqualTo(WH40KChaosPatron.Tzeentch));
            Assert.That(progression.PatronSelectionLocked, Is.True);
            Assert.That(progression.PrimaryGiftSlot, Is.EqualTo(0),
                "Patron change path must reset branch unlock state.");
            Assert.That(progression.GiftSlotOneUnlocked, Is.False);
            Assert.That(progression.GiftSlotTwoUnlocked, Is.False);
            Assert.That(progression.GiftSlotThreeUnlocked, Is.False);
        });
    }

    [Test]
    public void R7FreePrimaryAndPaidUnlockOrderingIsEnforced()
    {
        var progression = new WH40KChaosGiftProgressionComponent
        {
            AttunedPatron = WH40KChaosPatron.Khorne,
            PatronSelectionLocked = true,
            DevelopmentPoints = 3,
            GiftUnlockCost = 2,
        };

        var unlockBeforePrimary = TryUnlockGiftSlot(progression, 2);
        var selectPrimary = TrySelectPrimaryGiftSlot(progression, 1);
        var selectPrimaryAgain = TrySelectPrimaryGiftSlot(progression, 3);
        var unlockPrimarySlot = TryUnlockGiftSlot(progression, 1);
        var unlockSecondSlot = TryUnlockGiftSlot(progression, 2);
        var unlockSecondSlotAgain = TryUnlockGiftSlot(progression, 2);
        var unlockThirdNoPoints = TryUnlockGiftSlot(progression, 3);

        Assert.Multiple(() =>
        {
            Assert.That(unlockBeforePrimary, Is.EqualTo(GiftActionOutcome.PrimaryRequired));
            Assert.That(selectPrimary, Is.EqualTo(GiftActionOutcome.Success));
            Assert.That(selectPrimaryAgain, Is.EqualTo(GiftActionOutcome.PrimaryAlreadySet));
            Assert.That(unlockPrimarySlot, Is.EqualTo(GiftActionOutcome.PrimaryCannotPurchase));
            Assert.That(unlockSecondSlot, Is.EqualTo(GiftActionOutcome.Success));
            Assert.That(unlockSecondSlotAgain, Is.EqualTo(GiftActionOutcome.AlreadyUnlocked));
            Assert.That(unlockThirdNoPoints, Is.EqualTo(GiftActionOutcome.NotEnoughPoints));

            Assert.That(progression.PrimaryGiftSlot, Is.EqualTo(1));
            Assert.That(progression.GiftSlotOneUnlocked, Is.True);
            Assert.That(progression.GiftSlotTwoUnlocked, Is.True);
            Assert.That(progression.GiftSlotThreeUnlocked, Is.False);
            Assert.That(progression.DevelopmentPoints, Is.EqualTo(1));
        });
    }

    [Test]
    public void R7OwnershipIsolationKeepsProgressionPersonal()
    {
        var ownerA = EntityUid.Parse("1");
        var ownerB = EntityUid.Parse("2");

        var skrizhal = new WH40KChaosSkrizhalComponent
        {
            BindOnFirstUse = true,
            RestrictToBoundOwner = true,
        };

        var progressionA = new WH40KChaosGiftProgressionComponent
        {
            DevelopmentPoints = 4,
            GiftUnlockCost = 2,
        };

        var progressionB = new WH40KChaosGiftProgressionComponent
        {
            DevelopmentPoints = 4,
            GiftUnlockCost = 2,
        };

        var firstUse = TrySelectPatronViaSkrizhal(
            skrizhal,
            ownerA,
            progressionA,
            WH40KChaosPatron.Slaanesh);

        var foreignUse = TrySelectPatronViaSkrizhal(
            skrizhal,
            ownerB,
            progressionB,
            WH40KChaosPatron.Tzeentch);

        var ownerPrimary = TrySelectPrimaryGiftSlot(progressionA, 1);
        var ownerUnlock = TryUnlockGiftSlot(progressionA, 2);

        Assert.Multiple(() =>
        {
            Assert.That(firstUse, Is.EqualTo(PatronSelectionOutcome.Success));
            Assert.That(foreignUse, Is.EqualTo(PatronSelectionOutcome.OwnerMismatch));
            Assert.That(skrizhal.BoundOwner, Is.EqualTo(ownerA));

            Assert.That(ownerPrimary, Is.EqualTo(GiftActionOutcome.Success));
            Assert.That(ownerUnlock, Is.EqualTo(GiftActionOutcome.Success));

            Assert.That(progressionA.AttunedPatron, Is.EqualTo(WH40KChaosPatron.Slaanesh));
            Assert.That(progressionA.PrimaryGiftSlot, Is.EqualTo(1));
            Assert.That(progressionA.GiftSlotOneUnlocked, Is.True);
            Assert.That(progressionA.GiftSlotTwoUnlocked, Is.True);
            Assert.That(progressionA.GiftSlotThreeUnlocked, Is.False);
            Assert.That(progressionA.DevelopmentPoints, Is.EqualTo(2));

            Assert.That(progressionB.AttunedPatron, Is.EqualTo(WH40KChaosPatron.None));
            Assert.That(progressionB.PrimaryGiftSlot, Is.EqualTo(0));
            Assert.That(progressionB.GiftSlotOneUnlocked, Is.False);
            Assert.That(progressionB.GiftSlotTwoUnlocked, Is.False);
            Assert.That(progressionB.GiftSlotThreeUnlocked, Is.False);
            Assert.That(progressionB.DevelopmentPoints, Is.EqualTo(4));
            Assert.That(ShouldOpenPatronSelector(progressionB), Is.True);
        });
    }

    private static PatronSelectionOutcome TrySelectPatronViaSkrizhal(
        WH40KChaosSkrizhalComponent skrizhal,
        EntityUid actor,
        WH40KChaosGiftProgressionComponent progression,
        WH40KChaosPatron patron)
    {
        if (skrizhal.RestrictToBoundOwner &&
            skrizhal.BoundOwner is { } boundOwner &&
            boundOwner != actor)
        {
            return PatronSelectionOutcome.OwnerMismatch;
        }

        if (skrizhal.BoundOwner == null && skrizhal.BindOnFirstUse)
            skrizhal.BoundOwner = actor;

        return TrySelectPatron(progression, patron);
    }

    private static PatronSelectionOutcome TrySelectPatron(
        WH40KChaosGiftProgressionComponent progression,
        WH40KChaosPatron patron)
    {
        if (!IsSelectablePatron(patron))
            return PatronSelectionOutcome.InvalidPatron;

        if (progression.PatronSelectionLocked &&
            progression.AttunedPatron == patron &&
            !progression.AllowPatronSwitch)
        {
            return PatronSelectionOutcome.AlreadyAttuned;
        }

        if (progression.PatronSelectionLocked &&
            progression.AttunedPatron != WH40KChaosPatron.None &&
            progression.AttunedPatron != patron &&
            !progression.AllowPatronSwitch)
        {
            return PatronSelectionOutcome.SwitchBlocked;
        }

        var previousPatron = progression.AttunedPatron;
        var firstAttunement = progression.AttunedPatron == WH40KChaosPatron.None;

        progression.AttunedPatron = patron;
        progression.PatronSelectionLocked = true;

        if (firstAttunement || previousPatron != patron)
            ResetGiftUnlockState(progression);

        return PatronSelectionOutcome.Success;
    }

    private static GiftActionOutcome TrySelectPrimaryGiftSlot(
        WH40KChaosGiftProgressionComponent progression,
        int giftSlot)
    {
        if (progression.AttunedPatron == WH40KChaosPatron.None)
            return GiftActionOutcome.AttunementRequired;

        if (!IsValidGiftSlot(giftSlot))
            return GiftActionOutcome.InvalidSlot;

        if (progression.PrimaryGiftSlot != 0)
            return GiftActionOutcome.PrimaryAlreadySet;

        progression.PrimaryGiftSlot = giftSlot;
        SetGiftSlotUnlocked(progression, giftSlot, true);
        return GiftActionOutcome.Success;
    }

    private static GiftActionOutcome TryUnlockGiftSlot(
        WH40KChaosGiftProgressionComponent progression,
        int giftSlot)
    {
        if (progression.AttunedPatron == WH40KChaosPatron.None)
            return GiftActionOutcome.AttunementRequired;

        if (!IsValidGiftSlot(giftSlot))
            return GiftActionOutcome.InvalidSlot;

        if (progression.PrimaryGiftSlot == 0)
            return GiftActionOutcome.PrimaryRequired;

        if (progression.PrimaryGiftSlot == giftSlot)
            return GiftActionOutcome.PrimaryCannotPurchase;

        if (IsGiftSlotUnlocked(progression, giftSlot))
            return GiftActionOutcome.AlreadyUnlocked;

        var cost = Math.Max(1, progression.GiftUnlockCost);
        if (progression.DevelopmentPoints < cost)
            return GiftActionOutcome.NotEnoughPoints;

        progression.DevelopmentPoints -= cost;
        SetGiftSlotUnlocked(progression, giftSlot, true);
        return GiftActionOutcome.Success;
    }

    private static bool ShouldOpenPatronSelector(WH40KChaosGiftProgressionComponent progression)
    {
        return progression.AttunedPatron == WH40KChaosPatron.None ||
               !progression.PatronSelectionLocked ||
               progression.AllowPatronSwitch;
    }

    private static bool IsSelectablePatron(WH40KChaosPatron patron)
    {
        return patron is WH40KChaosPatron.Khorne or
               WH40KChaosPatron.Nurgle or
               WH40KChaosPatron.Slaanesh or
               WH40KChaosPatron.Tzeentch;
    }

    private static bool IsValidGiftSlot(int slot)
    {
        return slot is >= 1 and <= 3;
    }

    private static bool IsGiftSlotUnlocked(WH40KChaosGiftProgressionComponent progression, int slot)
    {
        return slot switch
        {
            1 => progression.GiftSlotOneUnlocked,
            2 => progression.GiftSlotTwoUnlocked,
            3 => progression.GiftSlotThreeUnlocked,
            _ => false,
        };
    }

    private static void SetGiftSlotUnlocked(WH40KChaosGiftProgressionComponent progression, int slot, bool value)
    {
        switch (slot)
        {
            case 1:
                progression.GiftSlotOneUnlocked = value;
                break;
            case 2:
                progression.GiftSlotTwoUnlocked = value;
                break;
            case 3:
                progression.GiftSlotThreeUnlocked = value;
                break;
        }
    }

    private static void ResetGiftUnlockState(WH40KChaosGiftProgressionComponent progression)
    {
        progression.PrimaryGiftSlot = 0;
        progression.GiftSlotOneUnlocked = false;
        progression.GiftSlotTwoUnlocked = false;
        progression.GiftSlotThreeUnlocked = false;
    }

    private enum PatronSelectionOutcome : byte
    {
        Success,
        InvalidPatron,
        AlreadyAttuned,
        SwitchBlocked,
        OwnerMismatch,
    }

    private enum GiftActionOutcome : byte
    {
        Success,
        AttunementRequired,
        InvalidSlot,
        PrimaryRequired,
        PrimaryAlreadySet,
        PrimaryCannotPurchase,
        AlreadyUnlocked,
        NotEnoughPoints,
    }
}
