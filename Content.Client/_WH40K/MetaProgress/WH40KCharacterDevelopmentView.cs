using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed class WH40KCharacterDevelopmentView : BoxContainer
{
	[Dependency]
	private readonly IEntityManager _entityManager = default!;

	[Dependency]
	private readonly IPrototypeManager _prototypeManager = default!;

	private const string RewardTableId = "WH40KMetaLevelRewardTableDefault";

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

	private readonly Label _zoomLabel;

	private readonly Button _resetViewButton;

	private readonly Button _confirmPlanButton;

	private readonly StyleBoxFlat _stateChipStyle;

	private readonly StyleBoxFlat _costChipStyle;

	private int _accountLevel = 1;

	public WH40KCharacterDevelopmentView()
	{
		IoCManager.InjectDependencies(this);
		base.Orientation = LayoutOrientation.Vertical;
		base.SeparationOverride = 12;
		base.HorizontalExpand = true;
		base.VerticalExpand = true;
		base.RectClipContent = true;
		base.Margin = new Thickness(4f);
		StyleBoxFlat panelOverride = CreatePanelStyle("#121B26", "#445767", 14, 12);
		StyleBoxFlat panelOverride2 = CreatePanelStyle("#17222D", "#516577", 12, 10);
		StyleBoxFlat panelOverride3 = CreatePanelStyle("#182531", "#597080", 12, 10);
		StyleBoxFlat panelOverride4 = CreatePanelStyle("#0E151E", "#495B6A", 6, 6);
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
			FontColorOverride = Color.FromHex("#91A6B6".AsSpan())
		};
		_branchLabel = new Label
		{
			StyleClasses = { "LabelHeading" },
			ClipText = true
		};
		_nodeLabel = new Label
		{
			FontColorOverride = Color.FromHex("#E7F2F7".AsSpan()),
			ClipText = true
		};
		_descriptionLabel = new RichTextLabel
		{
			HorizontalExpand = true,
			VerticalExpand = false,
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
			BackgroundColor = Color.FromHex("#233443".AsSpan()),
			BorderColor = Color.FromHex("#5A6F80".AsSpan()),
			BorderThickness = new Thickness(1f),
			ContentMarginLeftOverride = 8f,
			ContentMarginRightOverride = 8f,
			ContentMarginTopOverride = 4f,
			ContentMarginBottomOverride = 4f
		};
		_stateChipLabel = new Label();
		_stateChipContainer = new PanelContainer
		{
			PanelOverride = _stateChipStyle
		};
		_stateChipContainer.AddChild(_stateChipLabel);
		_costChipStyle = new StyleBoxFlat
		{
			BackgroundColor = Color.FromHex("#2B2A1B".AsSpan()),
			BorderColor = Color.FromHex("#A98C4C".AsSpan()),
			BorderThickness = new Thickness(1f),
			ContentMarginLeftOverride = 8f,
			ContentMarginRightOverride = 8f,
			ContentMarginTopOverride = 4f,
			ContentMarginBottomOverride = 4f
		};
		_costChipLabel = new Label();
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
		_zoomLabel = new Label();
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
		_sectionLabel.Text = Loc.GetString("wh40k-character-development-section-label");
		_resetViewButton.Text = Loc.GetString("wh40k-character-development-action-center");
		_confirmPlanButton.Text = Loc.GetString("wh40k-character-development-action-confirm");

		var hoverInfo = _viewport.CurrentHoverInfo;
		if (hoverInfo != null)
		{
			ApplyHoverInfo(hoverInfo);
		}
		else
		{
			ApplyDefaultInfo();
		}
		RefreshSummary();
	}

	private void OnHoverChanged(WH40KCharacterDevelopmentNodePresentation? presentation)
	{
		if (presentation == null)
		{
			ApplyDefaultInfo();
		}
		else
		{
			ApplyHoverInfo(presentation);
		}
		RefreshSummary();
	}

	private void OnPlannerChanged()
	{
		var hoverInfo = _viewport.CurrentHoverInfo;
		if (hoverInfo != null)
		{
			ApplyHoverInfo(hoverInfo);
		}
		else
		{
			ApplyDefaultInfo();
		}
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
		int num2 = _viewport.OpenedCost + _viewport.PlannedCost + _viewport.SubmittedCost;
		int num3 = Math.Max(0, num - num2);
		_levelLabel.Text = Loc.GetString("wh40k-character-development-summary-level", ("level", _accountLevel));
		_pointsLabel.Text = Loc.GetString("wh40k-character-development-summary-points", ("available", num3));
		_zoomLabel.Text = Loc.GetString("wh40k-character-development-summary-zoom", ("value", (int)MathF.Round(_viewport.ZoomPercent * 100f)));
		_confirmPlanButton.Disabled = _viewport.PlannedNodeCount == 0;
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
		_branchLabel.Text = Loc.GetString("wh40k-character-development-default-branch");
		_nodeLabel.Text = Loc.GetString("wh40k-character-development-default-node");
		_descriptionLabel.SetMessage(Loc.GetString("wh40k-character-development-default-description"), Color.FromHex("#C6D4DD".AsSpan()));
		_stateChipLabel.Text = Loc.GetString("wh40k-character-development-default-state");
		_costChipLabel.Text = string.Empty;
		_costChipContainer.Visible = false;
		_branchLabel.FontColorOverride = Color.FromHex("#E7F2F7".AsSpan());
		_stateChipStyle.BorderColor = Color.FromHex("#5A6F80".AsSpan());
		_stateChipStyle.BackgroundColor = Color.FromHex("#233443".AsSpan());
		_costChipStyle.BorderColor = Color.FromHex("#A98C4C".AsSpan());
		_costChipStyle.BackgroundColor = Color.FromHex("#2B2A1B".AsSpan());
	}

	private void ApplyHoverInfo(WH40KCharacterDevelopmentNodePresentation presentation)
	{
		_branchLabel.Text = Loc.GetString(presentation.BranchTitleKey);
		_nodeLabel.Text = Loc.GetString(presentation.NodeTitleKey);
		_descriptionLabel.SetMessage(Loc.GetString(presentation.DescriptionKey), Color.FromHex("#C6D4DD".AsSpan()));
		_stateChipLabel.Text = Loc.GetString(presentation.StateKey);
		_costChipLabel.Text = Loc.GetString("wh40k-character-development-cost-chip", ("cost", presentation.Cost));
		_costChipContainer.Visible = true;
		_branchLabel.FontColorOverride = presentation.Accent.WithAlpha(0.96f);
		_stateChipStyle.BorderColor = presentation.Accent.WithAlpha(0.95f);
		_stateChipStyle.BackgroundColor = Blend(Color.FromHex("#233443".AsSpan()), presentation.Accent.WithAlpha(0.22f), 0.55f);
		_costChipStyle.BorderColor = Blend(Color.FromHex("#A98C4C".AsSpan()), presentation.Accent.WithAlpha(0.65f), 0.35f);
		_costChipStyle.BackgroundColor = Blend(Color.FromHex("#2B2A1B".AsSpan()), presentation.Accent.WithAlpha(0.15f), 0.25f);
	}

	private static StyleBoxFlat CreatePanelStyle(string backgroundHex, string borderHex, int horizontalPadding, int verticalPadding)
	{
		return new StyleBoxFlat
		{
			BackgroundColor = Color.FromHex(backgroundHex.AsSpan()),
			BorderColor = Color.FromHex(borderHex.AsSpan()),
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
