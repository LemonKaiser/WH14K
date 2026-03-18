#nullable enable
using Content.Client._WH40K.TacticalMap.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WH40K.TacticalMap;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class TacticalMapUiIntegrationTests : InteractionTest
{
    [Test]
    public async Task CommandTacticalMapTabletUseOpensWindowAndRendersAnnotationControls()
    {
        var tablet = await PlaceInHands("WH40KCommandTacticalMapTablet");

        Assert.That(IsUiOpen(WH40KTacticalMapUiKey.Key), Is.False, "Tactical-map UI opened unexpectedly on pickup.");

        await UseInHand();
        await RunTicks(5);

        Assert.That(IsUiOpen(WH40KTacticalMapUiKey.Key), Is.True, "Tactical-map UI did not open on use-in-hand.");

        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<WH40KTacticalMapWindow>();
            var colorLabel = GetControlFromField<Label>("CurrentColorLabel", window);
            var saveButton = GetControlFromField<Button>("SaveAnnotationsButton", window);
            var clearButton = GetControlFromField<Button>("ClearAnnotationsButton", window);
            var alliesToggleButton = GetControlFromField<Button>("ToggleAlliesButton", window);
            var allyNamesToggleButton = GetControlFromField<Button>("ToggleAllyNamesButton", window);
            var gridToggleButton = GetControlFromField<Button>("ToggleGridButton", window);
            var mapControl = GetControlFromField<WH40KTacticalMapControl>("TacticalMapScreen", window);

            Assert.Multiple(() =>
            {
                Assert.That(window.IsOpen, Is.True, "Tactical-map window exists but is not open.");
                Assert.That(window.Title, Does.Contain("tactical map").IgnoreCase, "Unexpected tactical-map window title.");
                Assert.That(colorLabel.Text, Is.EqualTo(Color.Red.ToHexNoAlpha()), "Default tactical-map color label is wrong.");
                Assert.That(saveButton, Is.Not.Null, "Save button missing from tactical-map window.");
                Assert.That(clearButton, Is.Not.Null, "Clear button missing from tactical-map window.");
                Assert.That(saveButton.Visible, Is.True, "Command tablet unexpectedly hid save controls.");
                Assert.That(clearButton.Visible, Is.True, "Command tablet unexpectedly hid annotation clear controls.");
                Assert.That(alliesToggleButton, Is.Not.Null, "Allies toggle button missing from tactical-map window.");
                Assert.That(allyNamesToggleButton, Is.Not.Null, "Ally-name toggle button missing from tactical-map window.");
                Assert.That(gridToggleButton, Is.Not.Null, "Chunk-grid toggle button missing from tactical-map window.");
                Assert.That(mapControl, Is.Not.Null, "Map control missing from tactical-map window.");
                Assert.That(mapControl.ShowAllyNames, Is.False, "Tactical-map ally names should default to hover-only mode.");
            });
        });

        await CloseBui(WH40KTacticalMapUiKey.Key, tablet);
        await RunTicks(5);

        Assert.That(IsUiOpen(WH40KTacticalMapUiKey.Key), Is.False, "Tactical-map UI failed to close.");
    }

    [Test]
    public async Task StandardTacticalMapTabletUseOpensReadonlyLayout()
    {
        var tablet = await PlaceInHands("WH40KStandardTacticalMapTablet");

        Assert.That(IsUiOpen(WH40KTacticalMapUiKey.Key), Is.False, "Tactical-map UI opened unexpectedly on pickup.");

        await UseInHand();
        await RunTicks(5);

        Assert.That(IsUiOpen(WH40KTacticalMapUiKey.Key), Is.True, "Read-only tactical-map UI did not open on use-in-hand.");

        await Client.WaitAssertion(() =>
        {
            var window = GetWindow<WH40KTacticalMapWindow>();
            var leftRail = GetControlFromField<PanelContainer>("LeftRail", window);
            var statusPanel = GetControlFromField<PanelContainer>("StatusPanel", window);
            var annotationControlsStack = GetControlFromField<BoxContainer>("AnnotationControlsStack", window);
            var toolsPanel = GetControlFromField<PanelContainer>("ToolsPanel", window);
            var colorPanel = GetControlFromField<PanelContainer>("ColorPanel", window);
            var thicknessPanel = GetControlFromField<PanelContainer>("ThicknessPanel", window);
            var saveButton = GetControlFromField<Button>("SaveAnnotationsButton", window);
            var reloadButton = GetControlFromField<Button>("ReloadSavedButton", window);
            var clearButton = GetControlFromField<Button>("ClearAnnotationsButton", window);
            var draftBadge = GetControlFromField<PanelContainer>("DraftBadge", window);
            var footerLeftLabel = GetControlFromField<Label>("FooterLeftLabel", window);
            var toolSummaryLabel = GetControlFromField<Label>("ToolSummaryLabel", window);
            var overlaySyncLabel = GetControlFromField<Label>("OverlaySyncLabel", window);
            var annotationsStatusLabel = GetControlFromField<Label>("AnnotationsStatus", window);
            var allyNamesToggleButton = GetControlFromField<Button>("ToggleAllyNamesButton", window);
            var mapControl = GetControlFromField<WH40KTacticalMapControl>("TacticalMapScreen", window);

            Assert.Multiple(() =>
            {
                Assert.That(window.IsOpen, Is.True, "Read-only tactical-map window exists but is not open.");
                Assert.That(leftRail.SetWidth, Is.GreaterThanOrEqualTo(300f),
                    "Read-only tactical-map left rail is too narrow and causes status controls to spill out.");
                Assert.That(statusPanel.Visible, Is.True, "Read-only tactical tablet unexpectedly hid its status block.");
                Assert.That(annotationControlsStack.Visible, Is.False, "Read-only tactical tablet still shows the annotation control stack.");
                Assert.That(toolsPanel.Visible, Is.False, "Read-only tactical tablet still shows drawing tools.");
                Assert.That(colorPanel.Visible, Is.False, "Read-only tactical tablet still shows color palette.");
                Assert.That(thicknessPanel.Visible, Is.False, "Read-only tactical tablet still shows thickness controls.");
                Assert.That(saveButton.Visible, Is.False, "Read-only tactical tablet still shows save action.");
                Assert.That(reloadButton.Visible, Is.False, "Read-only tactical tablet still shows reload action.");
                Assert.That(clearButton.Visible, Is.False, "Read-only tactical tablet still shows clear action.");
                Assert.That(draftBadge.Visible, Is.False, "Read-only tactical tablet still shows draft badge.");
                Assert.That(toolSummaryLabel.ClipText, Is.True, "Read-only tactical tablet tool summary should clip instead of bleeding into the map.");
                Assert.That(overlaySyncLabel.ClipText, Is.True, "Read-only tactical tablet sync status should clip instead of bleeding into the map.");
                Assert.That(annotationsStatusLabel.ClipText, Is.True, "Read-only tactical tablet annotation status should clip instead of bleeding into the map.");
                Assert.That(allyNamesToggleButton, Is.Not.Null, "Read-only tactical tablet is missing ally-name toggle.");
                Assert.That(mapControl.ShowAllyNames, Is.False, "Read-only tactical tablet should also default to hover-only ally labels.");
                Assert.That(footerLeftLabel.Text, Does.Not.Contain("tool").IgnoreCase,
                    "Read-only tactical tablet footer still references drawing tools.");
            });
        });

        await CloseBui(WH40KTacticalMapUiKey.Key, tablet);
        await RunTicks(5);

        Assert.That(IsUiOpen(WH40KTacticalMapUiKey.Key), Is.False, "Read-only tactical-map UI failed to close.");
    }
}
