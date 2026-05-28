using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._WH40K.Command;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared._WH40K.MetaProgress;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.MetaProgress;

public sealed partial class WH40KCharacterDevelopmentView : BoxContainer
{
	[Dependency]
	private  IEntityManager _entityManager = default!;

	[Dependency]
	private  IPrototypeManager _prototypeManager = default!;

	private const string RewardTableId = "WH40KMetaLevelRewardTableDefault";
	private const float InfoDescriptionMaxWidth = 720f;

	private static readonly Color DevelopmentPanelBackground = WH40KCommandUiStyles.PanelBackgroundAlt;

	private static readonly Color DevelopmentPanelBorder = WH40KCommandUiStyles.StrongBorder;

	private static readonly Color DevelopmentCardBackground = WH40KCommandUiStyles.CardBackgroundAlt;

	private static readonly Color DevelopmentCardBackgroundAlt = WH40KCommandUiStyles.CardBackgroundMuted;

	private static readonly Color DevelopmentCardBorder = WH40KCommandUiStyles.MutedBorder;

	private static readonly Color DevelopmentSectionText = WH40KCommandUiStyles.MutedText;

	private static readonly Color DevelopmentTitleText = WH40KCommandUiStyles.DefaultAccent;

	private static readonly Color DevelopmentBodyText = Color.FromHex("#E6DEC7".AsSpan());

	private static readonly Color DevelopmentSoftText = WH40KCommandUiStyles.SoftText;

	private static readonly Color DevelopmentWarningText = Color.FromHex("#F0DDD1".AsSpan());

	private static readonly Color StateChipBackground = WH40KCommandUiStyles.ButtonBackgroundAlt;

	private static readonly Color CostChipBackground = WH40KCommandUiStyles.BadgeBackground;

	private readonly WH40KCharacterDevelopmentViewport _viewport;

	private readonly Label _sectionLabel;

	private readonly Label _branchLabel;

	private readonly Label _nodeLabel;

	private readonly PanelContainer _stateChipContainer;

	private readonly PanelContainer _costChipContainer;

	private readonly Label _stateChipLabel;

	private readonly Label _costChipLabel;

	private readonly RichTextLabel _descriptionLabel;

	private readonly Label _levelLabel;

	private readonly Label _pointsLabel;

	private readonly Label _plannedLabel;

	private readonly Label _remainingLabel;

	private readonly Label _zoomLabel;

	private readonly Button _resetViewButton;

	private readonly Button _confirmPlanButton;

	private readonly StyleBoxFlat _stateChipStyle;

	private readonly StyleBoxFlat _costChipStyle;

	private int _accountLevel = 1;

	private bool _confirmPlanHovered;

	public WH40KCharacterDevelopmentView()
	{
		IoCManager.InjectDependencies(this);
		base.Orientation = LayoutOrientation.Vertical;
		base.SeparationOverride = 12;
		base.HorizontalExpand = true;
		base.VerticalExpand = true;
		base.RectClipContent = true;
		base.Margin = new Thickness(4f);
		StyleBoxFlat panelOverride = CreatePanelStyle(DevelopmentPanelBackground, DevelopmentPanelBorder, 14, 12);
		StyleBoxFlat panelOverride2 = CreatePanelStyle(DevelopmentCardBackground, DevelopmentCardBorder, 12, 10);
		StyleBoxFlat panelOverride3 = CreatePanelStyle(DevelopmentCardBackgroundAlt, DevelopmentPanelBorder, 12, 10);
		StyleBoxFlat panelOverride4 = CreatePanelStyle(WH40KCommandUiStyles.PanelBackground, DevelopmentCardBorder, 6, 6);
		PanelContainer panelContainer = new PanelContainer
		{
			PanelOverride = panelOverride,
			HorizontalExpand = true,
			VerticalExpand = false,
			RectClipContent = true
		};
		BoxContainer boxContainer = new BoxContainer
		{
			Orientation = LayoutOrientation.Vertical,
			SeparationOverride = 10,
			HorizontalExpand = true
		};
		BoxContainer boxContainer2 = new BoxContainer
		{
			Orientation = LayoutOrientation.Horizontal,
			SeparationOverride = 10,
			HorizontalExpand = true
		};
		PanelContainer panelContainer2 = new PanelContainer
		{
			PanelOverride = panelOverride2,
			HorizontalExpand = true,
			SizeFlagsStretchRatio = 1.3f,
			RectClipContent = true
		};
		BoxContainer boxContainer3 = new BoxContainer
		{
			Orientation = LayoutOrientation.Vertical,
			SeparationOverride = 5,
			HorizontalExpand = true
		};
		_sectionLabel = new Label
		{
			FontColorOverride = DevelopmentSectionText
		};
		_branchLabel = new Label
		{
			StyleClasses = { "LabelHeading" },
			FontColorOverride = DevelopmentTitleText,
			ClipText = true
		};
		_nodeLabel = new Label
		{
			FontColorOverride = DevelopmentBodyText,
			ClipText = true
		};
		_descriptionLabel = new RichTextLabel
		{
			HorizontalExpand = true,
			HorizontalAlignment = HAlignment.Left,
			VerticalExpand = false,
			MaxWidth = InfoDescriptionMaxWidth,
			MinHeight = 44f,
			Visible = true
		};
		BoxContainer boxContainer4 = new BoxContainer
		{
			Orientation = LayoutOrientation.Horizontal,
			SeparationOverride = 6,
			HorizontalExpand = true
		};
		_stateChipStyle = new StyleBoxFlat
		{
			BackgroundColor = StateChipBackground,
			BorderColor = DevelopmentCardBorder,
			BorderThickness = new Thickness(1f),
			ContentMarginLeftOverride = 8f,
			ContentMarginRightOverride = 8f,
			ContentMarginTopOverride = 4f,
			ContentMarginBottomOverride = 4f
		};
		_stateChipLabel = new Label
		{
			FontColorOverride = DevelopmentBodyText
		};
		_stateChipContainer = new PanelContainer
		{
			PanelOverride = _stateChipStyle
		};
		_stateChipContainer.AddChild(_stateChipLabel);
		_costChipStyle = new StyleBoxFlat
		{
			BackgroundColor = CostChipBackground,
			BorderColor = DevelopmentTitleText.WithAlpha(0.82f),
			BorderThickness = new Thickness(1f),
			ContentMarginLeftOverride = 8f,
			ContentMarginRightOverride = 8f,
			ContentMarginTopOverride = 4f,
			ContentMarginBottomOverride = 4f
		};
		_costChipLabel = new Label
		{
			FontColorOverride = DevelopmentTitleText
		};
		_costChipContainer = new PanelContainer
		{
			PanelOverride = _costChipStyle
		};
		_costChipContainer.AddChild(_costChipLabel);
		boxContainer4.AddChild(_stateChipContainer);
		boxContainer4.AddChild(_costChipContainer);
		boxContainer3.AddChild(_sectionLabel);
		boxContainer3.AddChild(_branchLabel);
		boxContainer3.AddChild(_nodeLabel);
		boxContainer3.AddChild(_descriptionLabel);
		boxContainer3.AddChild(boxContainer4);
		panelContainer2.AddChild(boxContainer3);
		PanelContainer panelContainer3 = new PanelContainer
		{
			PanelOverride = panelOverride3,
			HorizontalExpand = false,
			MinWidth = 286f,
			RectClipContent = true
		};
		BoxContainer boxContainer5 = new BoxContainer
		{
			Orientation = LayoutOrientation.Vertical,
			SeparationOverride = 6,
			HorizontalExpand = true
		};
		_levelLabel = new Label();
		_pointsLabel = new Label();
		_plannedLabel = new Label();
		_remainingLabel = new Label();
		_zoomLabel = new Label();
		_levelLabel.FontColorOverride = DevelopmentTitleText;
		_pointsLabel.FontColorOverride = DevelopmentBodyText;
		_plannedLabel.FontColorOverride = DevelopmentBodyText;
		_remainingLabel.FontColorOverride = DevelopmentSoftText;
		_zoomLabel.FontColorOverride = DevelopmentSectionText;
		_resetViewButton = new Button
		{
			HorizontalExpand = true,
			MinWidth = 0f,
			ClipText = true
		};
		_confirmPlanButton = new Button
		{
			HorizontalExpand = true,
			MinWidth = 0f,
			ClipText = true
		};
		_viewport = new WH40KCharacterDevelopmentViewport();
		_resetViewButton.OnPressed += delegate
		{
			_viewport.ResetView();
			RefreshSummary();
		};
		_confirmPlanButton.OnPressed += delegate
		{
			List<string> list = _viewport.ConfirmPlan();
			if (list.Count > 0)
			{
				_entityManager.System<WH40KMetaProgressSystem>().ConfirmDevelopmentPlan(list);
			}
			RefreshSummary();
		};
		_confirmPlanButton.OnMouseEntered += delegate
		{
			_confirmPlanHovered = true;
			RefreshInfoPanel();
		};
		_confirmPlanButton.OnMouseExited += delegate
		{
			_confirmPlanHovered = false;
			RefreshInfoPanel();
		};
		BoxContainer boxContainer6 = new BoxContainer
		{
			Orientation = LayoutOrientation.Vertical,
			SeparationOverride = 6,
			HorizontalExpand = true
		};
		boxContainer6.AddChild(_resetViewButton);
		boxContainer6.AddChild(_confirmPlanButton);
		boxContainer5.AddChild(_levelLabel);
		boxContainer5.AddChild(_pointsLabel);
		boxContainer5.AddChild(_plannedLabel);
		boxContainer5.AddChild(_remainingLabel);
		boxContainer5.AddChild(_zoomLabel);
		boxContainer5.AddChild(boxContainer6);
		panelContainer3.AddChild(boxContainer5);
		boxContainer2.AddChild(panelContainer2);
		boxContainer2.AddChild(panelContainer3);
		boxContainer.AddChild(boxContainer2);
		panelContainer.AddChild(boxContainer);
		PanelContainer panelContainer4 = new PanelContainer
		{
			PanelOverride = panelOverride4,
			HorizontalExpand = true,
			VerticalExpand = true,
			RectClipContent = true
		};
		_viewport.HoverChanged += OnHoverChanged;
		_viewport.PlannerChanged += OnPlannerChanged;
		_viewport.ViewChanged += OnViewportChanged;
		panelContainer4.AddChild(_viewport);
		AddChild(panelContainer);
		AddChild(panelContainer4);
		Relocalize();
	}

	public void SetProfile(HumanoidCharacterProfile? profile, JobPrototype? jobOverride, bool showClothes)
	{
		_viewport.SetProfile(profile, jobOverride, showClothes);
	}

	public void ReloadProfile(HumanoidCharacterProfile? profile)
	{
		_viewport.ReloadProfile(profile);
	}

	public void ClearPreview()
	{
		_viewport.ClearPreview();
	}

	public void SetFromSnapshot(WH40KMetaProgressSnapshot snapshot)
	{
		_accountLevel = Math.Max(1, snapshot.Level);
		_viewport.SetAuthoritativeDevelopmentState(snapshot.Development);
		RefreshSummary();
	}

	public void SetPreviewFromPlaytime(TimeSpan overallPlaytime)
	{
		_accountLevel = Math.Max(1, PlayerMetaProgressPanel.CalculatePreviewProgress(overallPlaytime).Level);
		_viewport.SetAvailableSkillPoints(CalculateTotalSkillPoints(_accountLevel));
		RefreshSummary();
	}

	public void Relocalize()
	{
		_viewport.Relocalize();
		_sectionLabel.Text = Loc.GetString("w40k-cd-section-label");
		_resetViewButton.Text = Loc.GetString("w40k-cd-action-center");
		_confirmPlanButton.Text = Loc.GetString("w40k-cd-action-confirm");
		_resetViewButton.ToolTip = Loc.GetString("w40k-cd-action-center-tooltip");

		RefreshInfoPanel();
		RefreshSummary();
	}

	private void OnHoverChanged(WH40KCharacterDevelopmentNodePresentation? presentation)
	{
		RefreshInfoPanel();
		RefreshSummary();
	}

	private void OnPlannerChanged()
	{
		RefreshInfoPanel();
		RefreshSummary();
	}

	private void OnViewportChanged()
	{
		RefreshSummary();
	}

	private void RefreshSummary()
	{
		int num = _viewport.TotalSkillPoints;
		if (num <= 0)
		{
			num = CalculateTotalSkillPoints(_accountLevel);
			_viewport.SetAvailableSkillPoints(num);
		}
		int num2 = Math.Max(0, num - _viewport.OpenedCost - _viewport.SubmittedCost);
		int num3 = Math.Max(0, num2 - _viewport.PlannedCost);
		_levelLabel.Text = Loc.GetString("w40k-cd-summary-level", ("level", _accountLevel));
		_pointsLabel.Text = Loc.GetString("w40k-cd-summary-available-now", ("available", num2));
		_plannedLabel.Text = Loc.GetString("w40k-cd-summary-planned", ("nodes", _viewport.PlannedNodeCount), ("cost", _viewport.PlannedCost));
		_remainingLabel.Text = Loc.GetString("w40k-cd-summary-after-confirm", ("remaining", num3));
		_zoomLabel.Text = Loc.GetString("w40k-cd-summary-zoom", ("value", (int)MathF.Round(_viewport.ZoomPercent * 100f)));
		_confirmPlanButton.Disabled = _viewport.PlannedNodeCount == 0;
		_confirmPlanButton.ToolTip = Loc.GetString(_viewport.PlannedNodeCount == 0
			? "w40k-cd-action-confirm-tooltip-disabled"
			: "w40k-cd-action-confirm-tooltip");
	}

	private int CalculateTotalSkillPoints(int level)
	{
		if (!_prototypeManager.TryIndex(RewardTableId, out WH40KMetaLevelRewardTablePrototype? prototype) ||
			prototype == null)
		{
			return 0;
		}

		return WH40KMetaProgressMath.CalculateTotalSkillPointsForLevel(level, prototype);
	}

	private void ApplyDefaultInfo()
	{
		_branchLabel.Text = Loc.GetString("w40k-cd-default-branch");
		_nodeLabel.Text = Loc.GetString("w40k-cd-default-node");
		_descriptionLabel.SetMessage(Loc.GetString("w40k-cd-default-description"), DevelopmentBodyText);
		_stateChipLabel.Text = Loc.GetString("w40k-cd-default-state");
		_costChipLabel.Text = string.Empty;
		_costChipContainer.Visible = false;
		_branchLabel.FontColorOverride = DevelopmentTitleText;
		_stateChipStyle.BorderColor = DevelopmentCardBorder;
		_stateChipStyle.BackgroundColor = StateChipBackground;
		_costChipStyle.BorderColor = DevelopmentTitleText.WithAlpha(0.82f);
		_costChipStyle.BackgroundColor = CostChipBackground;
	}

	private void ApplyHoverInfo(WH40KCharacterDevelopmentNodePresentation presentation)
	{
		_branchLabel.Text = Loc.GetString(presentation.BranchTitleKey);
		_nodeLabel.Text = Loc.GetString(presentation.NodeTitleKey);
		var description = Loc.GetString(presentation.DescriptionKey);
		if (!string.IsNullOrWhiteSpace(presentation.DescriptionSupplement))
			description += "\n\n" + presentation.DescriptionSupplement;

		_descriptionLabel.SetMessage(description, DevelopmentBodyText);
		_stateChipLabel.Text = presentation.StateText;
		_costChipLabel.Text = Loc.GetString("w40k-cd-cost-chip", ("cost", presentation.Cost));
		_costChipContainer.Visible = true;
		_branchLabel.FontColorOverride = presentation.Accent.WithAlpha(0.96f);
		_stateChipStyle.BorderColor = Blend(DevelopmentCardBorder, presentation.Accent.WithAlpha(0.9f), 0.46f);
		_stateChipStyle.BackgroundColor = Blend(StateChipBackground, presentation.Accent.WithAlpha(0.18f), 0.34f);
		_costChipStyle.BorderColor = Blend(DevelopmentTitleText.WithAlpha(0.82f), presentation.Accent.WithAlpha(0.65f), 0.24f);
		_costChipStyle.BackgroundColor = Blend(CostChipBackground, presentation.Accent.WithAlpha(0.12f), 0.18f);
	}

	private void ApplyConfirmWarningInfo()
	{
		var warningAccent = WH40KCommandUiStyles.WarningBadge;
		var availableNow = Math.Max(0, _viewport.TotalSkillPoints - _viewport.OpenedCost - _viewport.SubmittedCost);
		var remainingAfterConfirm = Math.Max(0, availableNow - _viewport.PlannedCost);

		_branchLabel.Text = Loc.GetString("w40k-cd-confirm-title");
		_nodeLabel.Text = Loc.GetString("w40k-cd-confirm-node");
		_descriptionLabel.SetMessage(
			Loc.GetString(
				"w40k-cd-confirm-description",
				("nodes", _viewport.PlannedNodeCount),
				("cost", _viewport.PlannedCost),
				("remaining", remainingAfterConfirm)),
			DevelopmentWarningText);
		_stateChipLabel.Text = Loc.GetString("w40k-cd-confirm-state");
		_costChipLabel.Text = Loc.GetString(
			"w40k-cd-confirm-cost",
			("nodes", _viewport.PlannedNodeCount),
			("cost", _viewport.PlannedCost));
		_costChipContainer.Visible = true;
		_branchLabel.FontColorOverride = warningAccent;
		_stateChipStyle.BorderColor = warningAccent.WithAlpha(0.95f);
		_stateChipStyle.BackgroundColor = Blend(StateChipBackground, warningAccent.WithAlpha(0.2f), 0.42f);
		_costChipStyle.BorderColor = Blend(DevelopmentTitleText.WithAlpha(0.82f), warningAccent.WithAlpha(0.8f), 0.38f);
		_costChipStyle.BackgroundColor = Blend(CostChipBackground, warningAccent.WithAlpha(0.18f), 0.26f);
	}

	private void RefreshInfoPanel()
	{
		if (_confirmPlanHovered && _viewport.PlannedNodeCount > 0)
		{
			ApplyConfirmWarningInfo();
			return;
		}

		var hoverInfo = _viewport.CurrentHoverInfo;
		if (hoverInfo != null)
		{
			ApplyHoverInfo(hoverInfo);
			return;
		}

		ApplyDefaultInfo();
	}

	private static StyleBoxFlat CreatePanelStyle(Color background, Color border, int horizontalPadding, int verticalPadding)
	{
		return new StyleBoxFlat
		{
			BackgroundColor = background,
			BorderColor = border,
			BorderThickness = new Thickness(1f),
			ContentMarginLeftOverride = horizontalPadding,
			ContentMarginRightOverride = horizontalPadding,
			ContentMarginTopOverride = verticalPadding,
			ContentMarginBottomOverride = verticalPadding
		};
	}

	private static Color Blend(Color from, Color to, float amount)
	{
		float blend = MathHelper.Clamp01(amount);
		return new Color(MathHelper.Lerp(from.R, to.R, blend), MathHelper.Lerp(from.G, to.G, blend), MathHelper.Lerp(from.B, to.B, blend), MathHelper.Lerp(from.A, to.A, blend));
	}
}
