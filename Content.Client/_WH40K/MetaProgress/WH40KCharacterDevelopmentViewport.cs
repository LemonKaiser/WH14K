using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._WH40K.Command;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared._WH40K.MetaProgress;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.MetaProgress;

public sealed partial class WH40KCharacterDevelopmentViewport : LayoutContainer
{
	private enum BranchLane : byte
	{
		Top,
		Middle,
		Bottom
	}

	private sealed class BranchDefinition
	{
		public required string Id;

		public required string TitleKey;

		public required string SubtitleKey;

		public required WH40KCharacterDevelopmentOrganType Organ;

		public required Color Accent;

		public required bool LeftSide;

		public required BranchLane Lane;

		public required Vector2 RootPosition;

		public required string RootNodeId;

		public required string[] UpperPath;

		public required string[] LowerPath;
	}

	private sealed class NodeDefinition
	{
		public required string Id;

		public required string BranchId;

		public required string TitleKey;

		public required string DescriptionKey;

		public required int Cost;

		public string? ParentId;

		public required Vector2 WorldPosition;

		public required bool IsRoot;
	}

	private enum NodeVisualState : byte
	{
		Available,
		Opened,
		Planned,
		LockedByParent,
		LockedByPoints
	}

	[Dependency]
	private  IResourceCache _resources = default!;

	private static readonly Vector2 NodeSize = new Vector2(220f, 100f);

	private static readonly Vector2 RootNodeSize = new Vector2(246f, 112f);

	private static readonly Vector2 DollSize = new Vector2(520f, 820f);

	private const float NodeHorizontalGap = 78f;

	private const float BranchRowGapY = 92f;

	private const float BranchLanePaddingY = 10f;

	private const float BranchRootInsetX = 0.45f;

	private const float ContentBoundsPadding = 120f;

	private const float BranchHeaderWidthRatio = 0.58f;

	private const float BranchHeaderHeightRatio = 0.5f;

	private const float WorldNodeTitleScale = 1.02f;

	private const float WorldRootTitleScale = 1.1f;

	private const float WorldChipScale = 0.82f;

	private const float NodeWorldWidthStep = 18f;

	private const float NodeWorldWidthMax = 340f;

	private const float RootWorldWidthMax = 390f;

	private static readonly Color CanvasBackgroundColor = WH40KCommandUiStyles.PanelBackground;

	private static readonly Color CanvasBorderColor = WH40KCommandUiStyles.StrongBorder.WithAlpha(0.72f);

	private static readonly Color GridColor = WH40KCommandUiStyles.MutedBorder.WithAlpha(0.08f);

	private static readonly Color DesignFrameColor = WH40KCommandUiStyles.StrongBorder.WithAlpha(0.34f);

	private static readonly Color CardBackgroundColor = WH40KCommandUiStyles.CardBackgroundAlt;

	private static readonly Color CardMutedBackgroundColor = WH40KCommandUiStyles.CardBackgroundMuted;

	private static readonly Color CardLockedBorderColor = WH40KCommandUiStyles.MutedBorder;

	private static readonly Color CardDeniedColor = Color.FromHex("#b70000".AsSpan());

	private static readonly Color TextPrimaryColor = Color.FromHex("#E6DEC7".AsSpan());

	private static readonly Color TextMutedColor = WH40KCommandUiStyles.MutedText;

	private static readonly Color RootConnectorColor = WH40KCommandUiStyles.DefaultAccent;

	private const float MinZoom = 1.04f;

	private const float MaxZoom = 3.04f;

	private const float DefaultZoom = 1f;

	private const float DragThreshold = 8f;

	private readonly Font _headerFont;

	private readonly Font _nodeFont;

	private readonly Font _chipFont;

	private readonly WH40KCharacterDevelopmentDollView _dollView;

	private readonly Dictionary<string, BranchDefinition> _branches = new Dictionary<string, BranchDefinition>();

	private readonly Dictionary<string, NodeDefinition> _nodes = new Dictionary<string, NodeDefinition>();

	private readonly Dictionary<string, List<string>> _childrenByParent = new Dictionary<string, List<string>>();

	private readonly Dictionary<string, Vector2> _nodeWorldSizes = new Dictionary<string, Vector2>();

	private readonly Dictionary<string, UIBox2> _nodeBoxes = new Dictionary<string, UIBox2>();

	private readonly Dictionary<string, float> _acceptPulses = new Dictionary<string, float>();

	private readonly Dictionary<string, float> _denyPulses = new Dictionary<string, float>();

	private readonly HashSet<string> _openedNodes = new HashSet<string>();

	private readonly HashSet<string> _plannedNodes = new HashSet<string>();

	private readonly HashSet<string> _submittedNodes = new HashSet<string>();

	private readonly Dictionary<string, string> _branchTitles = new Dictionary<string, string>();

	private readonly Dictionary<string, string> _branchSubtitles = new Dictionary<string, string>();

	private readonly Dictionary<string, string> _nodeTitles = new Dictionary<string, string>();

	private readonly Dictionary<string, string> _nodeCostChips = new Dictionary<string, string>();

	private string? _hoveredNodeId;

	private string _nodeCostLabel = string.Empty;

	private Vector2 _pan;

	private Vector2 _targetPan;

	private Vector2 _dragLastPointerPixel;

	private float _dragDistanceAccumulator;

	private float _zoom = 1f;

	private float _targetZoom = 1f;

	private float _time;

	private float _fitScale = 1f;

	private bool _panArmed;

	private bool _dragging;

	private bool _dragMoved;

	private bool _worldLayoutDirty = true;

	private Vector2 _screenCenter;

	private Vector2 _canvasCenter;

	private UIBox2 _dollBox;

	private UIBox2 _contentWorldBounds;

	private Vector2 _contentWorldCenter;

	private float _dollScale = 1f;

	public WH40KCharacterDevelopmentNodePresentation? CurrentHoverInfo { get; private set; }

	public int OpenedCost => _openedNodes.Sum((string id) => _nodes[id].Cost);

	public int PlannedCost => _plannedNodes.Sum((string id) => _nodes[id].Cost);

	public int SubmittedCost => _submittedNodes.Sum((string id) => _nodes[id].Cost);

	public int OpenedNodeCount => _openedNodes.Count;

	public int PlannedNodeCount => _plannedNodes.Count;

	public float ZoomPercent => _zoom;

	public int TotalSkillPoints { get; private set; }

	public event Action<WH40KCharacterDevelopmentNodePresentation?>? HoverChanged;

	public event Action? PlannerChanged;

	public event Action? ViewChanged;

	public WH40KCharacterDevelopmentViewport()
	{
		IoCManager.InjectDependencies(this);
		base.RectClipContent = true;
		base.MouseFilter = MouseFilterMode.Stop;
		base.HorizontalExpand = true;
		base.VerticalExpand = true;
		base.MinHeight = 0f;
		base.MinWidth = 0f;
		_headerFont = new VectorFont(_resources.GetResource<FontResource>("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"), 17);
		_nodeFont = new VectorFont(_resources.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 11);
		_chipFont = new VectorFont(_resources.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 10);
		_dollView = new WH40KCharacterDevelopmentDollView();
		AddChild(_dollView);
		BuildDefinitions();
		RefreshLocalizedTextCache();
		ResetView();
	}

	public void Relocalize()
	{
		RefreshLocalizedTextCache();
		_worldLayoutDirty = true;
		InvalidateArrange();
		RefreshHoverPresentation();
	}

	public void SetProfile(HumanoidCharacterProfile? profile, JobPrototype? jobOverride, bool showClothes)
	{
		_dollView.SetProfile(profile, jobOverride, showClothes);
	}

	public void ReloadProfile(HumanoidCharacterProfile? profile)
	{
		_dollView.ReloadProfile(profile);
	}

	public void ClearPreview()
	{
		_dollView.ClearPreview();
	}

	public void SetAvailableSkillPoints(int totalSkillPoints)
	{
		totalSkillPoints = Math.Max(0, totalSkillPoints);
		if (TotalSkillPoints != totalSkillPoints)
		{
			TotalSkillPoints = totalSkillPoints;
			RefreshHoverPresentation();
		}
	}

	public void SetAuthoritativeDevelopmentState(WH40KMetaDevelopmentSnapshot development)
	{
		TotalSkillPoints = Math.Max(0, development.TotalSkillPoints);
		_openedNodes.Clear();
		foreach (string openedNodeId in development.OpenedNodeIds)
		{
			if (_nodes.ContainsKey(openedNodeId))
			{
				_openedNodes.Add(openedNodeId);
			}
		}
		_submittedNodes.Clear();
		_plannedNodes.RemoveWhere((string id) => _openedNodes.Contains(id) || !_nodes.ContainsKey(id));
		RefreshHoverPresentation();
		this.PlannerChanged?.Invoke();
	}

	public void ResetView()
	{
		_panArmed = false;
		_pan = Vector2.Zero;
		_targetPan = Vector2.Zero;
		_zoom = 1f;
		_targetZoom = 1f;
		_dragging = false;
		_dragMoved = false;
		InvalidateArrange();
	}

	public void ClearPlan()
	{
		ClearPlanInternal(emitEvent: true);
	}

	public List<string> ConfirmPlan()
	{
		if (_plannedNodes.Count == 0)
		{
			return new List<string>();
		}
		List<string> list = new List<string>(_plannedNodes);
		list.Sort(delegate(string leftId, string rightId)
		{
			int num = string.CompareOrdinal(_nodes[leftId].BranchId, _nodes[rightId].BranchId);
			return (num == 0) ? string.CompareOrdinal(leftId, rightId) : num;
		});
		foreach (string item in list)
		{
			_submittedNodes.Add(item);
			_acceptPulses[item] = 1f;
		}
		_plannedNodes.Clear();
		RefreshHoverPresentation();
		this.PlannerChanged?.Invoke();
		return list;
	}

	protected override void KeyBindDown(GUIBoundKeyEventArgs args)
	{
		base.KeyBindDown(args);
		if (!(args.Function != EngineKeyFunctions.Use))
		{
			_panArmed = true;
			_dragging = false;
			_dragMoved = false;
			_dragLastPointerPixel = args.RelativePixelPosition;
			_dragDistanceAccumulator = 0f;
		}
	}

	protected override void KeyBindUp(GUIBoundKeyEventArgs args)
	{
		base.KeyBindUp(args);
		if (args.Function == EngineKeyFunctions.Use)
		{
			_panArmed = false;
			_dragging = false;
		}
		else if (!(args.Function != EngineKeyFunctions.UIClick) && !_dragMoved)
		{
			TryActivateNode(args.RelativePixelPosition);
		}
	}

	protected override void MouseMove(GUIMouseMoveEventArgs args)
	{
		base.MouseMove(args);
		if (_panArmed)
		{
			Vector2 relativePixelPosition = args.RelativePixelPosition;
			Vector2 vector = relativePixelPosition - _dragLastPointerPixel;
			_dragLastPointerPixel = relativePixelPosition;
			_dragDistanceAccumulator += vector.Length();
			if (!_dragMoved && _dragDistanceAccumulator >= 8f)
			{
				_dragMoved = true;
				_dragging = true;
			}
			if (_dragging)
			{
				_pan += vector;
				_targetPan = _pan;
				InvalidateArrange();
				UpdateHoverFromMousePosition();
			}
			else
			{
				UpdateHoveredNode(relativePixelPosition);
			}
		}
		else
		{
			UpdateHoveredNode(args.RelativePixelPosition);
		}
	}

	protected override void MouseWheel(GUIMouseWheelEventArgs args)
	{
		base.MouseWheel(args);
		_targetZoom = Math.Clamp(_targetZoom + args.Delta.Y * 0.08f, 1.04f, 3.04f);
	}

	protected override void MouseExited()
	{
		base.MouseExited();
		SetHoveredNode(null);
	}

	protected override void FrameUpdate(FrameEventArgs args)
	{
		base.FrameUpdate(args);
		float num = args.DeltaSeconds;
		if (num <= 0f)
		{
			return;
		}
		_time += num;
		bool flag = false;
		bool flag2 = false;
		if (!_dragging)
		{
			float amount = MathHelper.Clamp01(num * 10f);
			Vector2 vector = Vector2.Lerp(_pan, _targetPan, amount);
			if ((vector - _pan).LengthSquared() > 0.01f)
			{
				_pan = vector;
				flag = true;
				flag2 = true;
			}
			else if (_pan != _targetPan)
			{
				_pan = _targetPan;
				flag = true;
				flag2 = true;
			}
		}
		float blend = MathHelper.Clamp01(num * 10f);
		float num2 = MathHelper.Lerp(_zoom, _targetZoom, blend);
		if (MathF.Abs(num2 - _zoom) > 0.001f)
		{
			_zoom = num2;
			flag = true;
			flag2 = true;
		}
		else if (!MathHelper.CloseTo(_zoom, _targetZoom))
		{
			_zoom = _targetZoom;
			flag = true;
			flag2 = true;
		}
		flag |= AdvancePulseMap(_acceptPulses, num * 2.8f);
		if (flag | AdvancePulseMap(_denyPulses, num * 3.2f))
		{
			InvalidateArrange();
			UpdateHoverFromMousePosition();
		}
		if (flag2)
		{
			this.ViewChanged?.Invoke();
		}
	}

	protected override Vector2 ArrangeOverride(Vector2 finalSize)
	{
		UpdateScreenLayout(finalSize * UIScale);
		return base.ArrangeOverride(finalSize);
	}

	protected override void Draw(DrawingHandleScreen handle)
	{
		base.Draw(handle);
		UpdateScreenLayout(new Vector2(base.PixelWidth, base.PixelHeight));
		DrawBackground(handle);
		DrawDesignFrame(handle);
		DrawBranchConnectors(handle);
		DrawBranchHeaders(handle);
		DrawNodes(handle);
	}

	private void BuildDefinitions()
	{
		_branches.Clear();
		_nodes.Clear();
		_childrenByParent.Clear();
		foreach (WH40KMetaDevelopmentBranchDefinition item in WH40KMetaDevelopmentCatalog.Branches.OrderBy((WH40KMetaDevelopmentBranchDefinition branch) => branch.SortOrder))
		{
			AddBranch(item);
		}
		_worldLayoutDirty = true;
		RefreshLocalizedTextCache();
	}

	private void AddBranch(WH40KMetaDevelopmentBranchDefinition sharedBranch)
	{
		(Color Accent, bool LeftSide, BranchLane Lane) tuple = ResolveBranchPresentation(sharedBranch.Id);
		Color item = tuple.Accent;
		bool item2 = tuple.LeftSide;
		BranchLane item3 = tuple.Lane;
		BranchDefinition value = new BranchDefinition
		{
			Id = sharedBranch.Id,
			TitleKey = $"w40k-cd-{sharedBranch.Id}-branch-title",
			SubtitleKey = $"w40k-cd-{sharedBranch.Id}-branch-subtitle",
			Organ = sharedBranch.Organ,
			Accent = item,
			LeftSide = item2,
			Lane = item3,
			RootPosition = Vector2.Zero,
			RootNodeId = sharedBranch.RootNodeId,
			UpperPath = sharedBranch.UpperPathNodeIds.ToArray(),
			LowerPath = sharedBranch.LowerPathNodeIds.ToArray()
		};
		AddNode(sharedBranch.RootNodeId);
		foreach (string upperPathNodeId in sharedBranch.UpperPathNodeIds)
		{
			AddNode(upperPathNodeId);
		}
		foreach (string lowerPathNodeId in sharedBranch.LowerPathNodeIds)
		{
			AddNode(lowerPathNodeId);
		}
		_branches[sharedBranch.Id] = value;
	}

	private void AddNode(string id)
	{
		var sharedNodeDefinition = WH40KMetaDevelopmentCatalog.Nodes[id];
		NodeDefinition value = new NodeDefinition
		{
			Id = sharedNodeDefinition.Id,
			BranchId = sharedNodeDefinition.BranchId,
			TitleKey = $"w40k-cd-{sharedNodeDefinition.BranchId}-node-{sharedNodeDefinition.NodeKey}-title",
			DescriptionKey = $"w40k-cd-{sharedNodeDefinition.BranchId}-node-{sharedNodeDefinition.NodeKey}-description",
			Cost = sharedNodeDefinition.Cost,
			ParentId = sharedNodeDefinition.ParentId,
			WorldPosition = Vector2.Zero,
			IsRoot = sharedNodeDefinition.IsRoot
		};
		_nodes[id] = value;

		var parentId = sharedNodeDefinition.ParentId;
		if (!string.IsNullOrWhiteSpace(parentId))
		{
			if (!_childrenByParent.TryGetValue(parentId, out var value2) || value2 == null)
			{
				value2 = new List<string>();
				_childrenByParent[parentId] = value2;
			}

			value2.Add(id);
		}
	}

	private static (Color Accent, bool LeftSide, BranchLane Lane) ResolveBranchPresentation(string branchId)
	{
		return branchId switch
		{
			"brain" => (Accent: Color.FromHex("#7EC8FF".AsSpan()), LeftSide: true, Lane: BranchLane.Top),
			"lungs" => (Accent: Color.FromHex("#73E3C7".AsSpan()), LeftSide: true, Lane: BranchLane.Middle),
			"kidneys" => (Accent: Color.FromHex("#8FD77A".AsSpan()), LeftSide: true, Lane: BranchLane.Bottom),
			"heart" => (Accent: Color.FromHex("#E86968".AsSpan()), LeftSide: false, Lane: BranchLane.Top),
			"liver" => (Accent: Color.FromHex("#D4A757".AsSpan()), LeftSide: false, Lane: BranchLane.Middle),
			"stomach" => (Accent: Color.FromHex("#8C6239".AsSpan()), LeftSide: false, Lane: BranchLane.Bottom),
			_ => (Accent: Color.White, LeftSide: true, Lane: BranchLane.Middle),
		};
	}

	private void UpdateScreenLayout(Vector2 finalSize)
	{
		if (finalSize.X <= 0f || finalSize.Y <= 0f)
		{
			return;
		}
		EnsureWorldLayout();
		Vector2 vector = new Vector2(MathF.Max(1f, finalSize.X - 24f), MathF.Max(1f, finalSize.Y - 24f));
		float num = MathF.Max(1f, _contentWorldBounds.Width);
		float num2 = MathF.Max(1f, _contentWorldBounds.Height);
		_fitScale = MathF.Max(0.1f, MathF.Min(vector.X / num, vector.Y / num2));
		float num3 = _fitScale * _zoom;
		_screenCenter = finalSize * 0.5f;
		_canvasCenter = _screenCenter + _pan;
		_dollScale = num3;
		_nodeBoxes.Clear();
		foreach (NodeDefinition value in _nodes.Values)
		{
			Vector2 size = _nodeWorldSizes.GetValueOrDefault(value.Id, value.IsRoot ? RootNodeSize : NodeSize) * num3;
			Vector2 center = TransformWorld(value.WorldPosition);
			_nodeBoxes[value.Id] = BoxFromCenter(center, size);
		}
		_dollBox = BoxFromCenter(TransformWorld(Vector2.Zero), DollSize * _dollScale);
		ApplyDollLayout();
	}

	private void EnsureWorldLayout()
	{
		if (_worldLayoutDirty)
		{
			RecalculateNodeWorldSizes();
			RecalculateBranchWorldLayout();
			RecalculateContentWorldBounds();
			_worldLayoutDirty = false;
		}
	}

	private void RefreshLocalizedTextCache()
	{
		_branchTitles.Clear();
		_branchSubtitles.Clear();
		_nodeTitles.Clear();
		_nodeCostChips.Clear();
		_nodeCostLabel = Loc.GetString("w40k-cd-node-cost-label");
		foreach (BranchDefinition value in _branches.Values)
		{
			_branchTitles[value.Id] = Loc.GetString(value.TitleKey);
			_branchSubtitles[value.Id] = Loc.GetString(value.SubtitleKey);
		}
		foreach (NodeDefinition value2 in _nodes.Values)
		{
			_nodeTitles[value2.Id] = Loc.GetString(value2.TitleKey);
			_nodeCostChips[value2.Id] = Loc.GetString("w40k-cd-node-cost-short", ("cost", value2.Cost));
		}
	}

	private void ApplyDollLayout()
	{
		Vector2 vector = _dollBox.TopLeft / UIScale;
		Vector2 vector2 = (_dollBox.BottomRight - _dollBox.TopLeft) / UIScale;
		if (!ApproximatelyEqual(_dollView.Position, vector))
		{
			LayoutContainer.SetPosition(_dollView, vector);
		}
		if (!ApproximatelyEqual(_dollView.SetSize, vector2))
		{
			_dollView.SetSize = vector2;
		}
	}

	private void DrawBackground(DrawingHandleScreen handle)
	{
		handle.DrawRect(base.PixelSizeBox, CanvasBackgroundColor);
		for (int i = -base.PixelHeight; i < base.PixelWidth + base.PixelHeight; i += 30)
		{
			Vector2 vector = new Vector2(i, 0f);
			Vector2 to = new Vector2(i + base.PixelHeight, base.PixelHeight);
			handle.DrawLine(vector, to, GridColor);
		}
		Vector2 screenCenter = _screenCenter;
		float num = 0.5f + 0.5f * MathF.Sin(_time * 1.9f);
		handle.DrawCircle(screenCenter, MathF.Min(base.PixelWidth, base.PixelHeight) * 0.15f, Color.FromHex("#6A5530".AsSpan()).WithAlpha(0.05f + num * 0.02f));
		handle.DrawCircle(screenCenter, MathF.Min(base.PixelWidth, base.PixelHeight) * 0.23f, WH40KCommandUiStyles.DefaultAccent.WithAlpha(0.035f));
		handle.DrawCircle(screenCenter, MathF.Min(base.PixelWidth, base.PixelHeight) * 0.31f, Color.FromHex("#4B3E25".AsSpan()).WithAlpha(0.03f));
	}

	private void DrawDesignFrame(DrawingHandleScreen handle)
	{
		Vector2 leftTop = TransformWorld(_contentWorldBounds.TopLeft);
		Vector2 rightBottom = TransformWorld(_contentWorldBounds.BottomRight);
		UIBox2 rect = new UIBox2(leftTop, rightBottom);
		handle.DrawRect(rect, DesignFrameColor, filled: false);
		handle.DrawRect(base.PixelSizeBox, CanvasBorderColor, filled: false);
	}

	private void DrawBranchConnectors(DrawingHandleScreen handle)
	{
		foreach (BranchDefinition value in _branches.Values)
		{
			bool flag = _hoveredNodeId != null && _nodes[_hoveredNodeId].BranchId == value.Id;
			float newA = (flag ? 0.92f : 0.48f);
			Color color = value.Accent.WithAlpha(newA);
			UIBox2 uIBox = _nodeBoxes[value.RootNodeId];
			Vector2 start = new Vector2(value.LeftSide ? uIBox.Right : uIBox.Left, uIBox.Center.Y);
			Vector2 vector = TransformOrgan(value.Organ);
			DrawConnector(handle, start, vector, color, value.LeftSide, emphasize: true);
			DrawPath(handle, value.RootNodeId, value.UpperPath, color, value.LeftSide);
			DrawPath(handle, value.RootNodeId, value.LowerPath, color, value.LeftSide);
			handle.DrawCircle(vector, 15f + (flag ? 5f : 0f), value.Accent.WithAlpha(flag ? 0.22f : 0.12f));
			handle.DrawCircle(vector, 7.5f + (flag ? 1.5f : 0f), value.Accent.WithAlpha(0.88f));
		}
	}

	private void DrawPath(DrawingHandleScreen handle, string rootId, IReadOnlyList<string> nodeIds, Color color, bool leftSide)
	{
		string nodeId = rootId;
		foreach (string nodeId2 in nodeIds)
		{
			Vector2 pathAnchor = GetPathAnchor(nodeId, leftSide, entrance: false);
			Vector2 pathAnchor2 = GetPathAnchor(nodeId2, leftSide, entrance: true);
			DrawConnector(handle, pathAnchor, pathAnchor2, color, leftSide, emphasize: false);
			nodeId = nodeId2;
		}
	}

	private void DrawBranchHeaders(DrawingHandleScreen handle)
	{
		foreach (BranchDefinition value in _branches.Values)
		{
			float num = _fitScale * _zoom;
			UIBox2 uIBox = BoxFromCenter(GetBranchHeaderCenter(value), GetBranchHeaderWorldSize(value) * num);
			ResolveBranchTypography(handle, value, uIBox, num, out string[] titleLines, out string subtitle, out float titleScale, out float subtitleScale);
			bool flag = _hoveredNodeId != null && _nodes[_hoveredNodeId].BranchId == value.Id;
			Color color = Blend(CardBackgroundColor, value.Accent.WithAlpha(0.28f), flag ? 0.55f : 0.26f);
			handle.DrawRect(uIBox, color);
			handle.DrawRect(uIBox, value.Accent.WithAlpha(flag ? 0.95f : 0.62f), filled: false);
			float y = handle.GetDimensions(_nodeFont, subtitle.AsSpan(), subtitleScale).Y;
			float num2 = MathF.Round(uIBox.Top + uIBox.Height * 0.62f);
			float y2 = uIBox.Bottom - y - 12f;
			float y3 = MathF.Min(num2 + 7f, y2);
			DrawLinesInBox(box: new UIBox2(uIBox.Left + 14f, uIBox.Top + 10f, uIBox.Right - 14f, num2 - 8f), handle: handle, font: _headerFont, lines: titleLines, color: TextPrimaryColor, scale: titleScale, lineHeightMultiplier: 0.86f);
			handle.DrawString(pos: new Vector2(uIBox.Left + 14f, y3), font: _nodeFont, str: subtitle.AsSpan(), scale: subtitleScale, color: TextMutedColor);
			Vector2 vector = Snap(new Vector2(uIBox.Left + 14f, num2));
			Vector2 to = Snap(new Vector2(uIBox.Right - 18f, num2));
			handle.DrawLine(vector, to, value.Accent.WithAlpha(0.75f));
		}
	}

	private void DrawNodes(DrawingHandleScreen handle)
	{
		foreach (NodeDefinition item in _nodes.Values.OrderBy((NodeDefinition def) => (!def.IsRoot) ? 1 : 0))
		{
			NodeVisualState nodeVisualState = ResolveNodeState(item);
			float scale = _fitScale * _zoom;
			UIBox2 uIBox = _nodeBoxes[item.Id];
			bool hovered = _hoveredNodeId == item.Id;
			float valueOrDefault = _acceptPulses.GetValueOrDefault(item.Id);
			float valueOrDefault2 = _denyPulses.GetValueOrDefault(item.Id);
			BranchDefinition branchDefinition = _branches[item.BranchId];
			(Color, Color, Color) tuple = ResolveNodeColors(branchDefinition.Accent, nodeVisualState, hovered, valueOrDefault, valueOrDefault2, item.IsRoot);
			ResolveNodeTypography(handle, item, uIBox, scale, out string[] titleLines, out string footerText, out string chipText, out float titleScale, out float chipScale);
			handle.DrawRect(uIBox, tuple.Item1);
			handle.DrawRect(uIBox, tuple.Item2, filled: false);
			Color color = tuple.Item2.WithAlpha(0.85f);
			handle.DrawLine(uIBox.TopLeft, uIBox.TopLeft + new Vector2(18f, 0f), color);
			handle.DrawLine(uIBox.TopLeft, uIBox.TopLeft + new Vector2(0f, 18f), color);
			handle.DrawLine(uIBox.BottomRight, uIBox.BottomRight - new Vector2(18f, 0f), color);
			handle.DrawLine(uIBox.BottomRight, uIBox.BottomRight - new Vector2(0f, 18f), color);
			if (valueOrDefault > 0f)
			{
				UIBox2 rect = ExpandBox(uIBox, 4f + valueOrDefault * 8f);
				handle.DrawRect(rect, branchDefinition.Accent.WithAlpha(valueOrDefault * 0.18f), filled: false);
			}
			if (valueOrDefault2 > 0f)
			{
				UIBox2 rect2 = ExpandBox(uIBox, 2f + valueOrDefault2 * 5f);
				handle.DrawRect(rect2, CardDeniedColor.WithAlpha(valueOrDefault2 * 0.26f), filled: false);
			}
			Vector2 dimensions = handle.GetDimensions(_chipFont, chipText.AsSpan(), chipScale);
			Vector2 dimensions2 = handle.GetDimensions(_chipFont, footerText.AsSpan(), chipScale);
			float num = MathF.Max(dimensions.Y, dimensions2.Y);
			DrawLinesInBox(box: new UIBox2(uIBox.Left + 12f, uIBox.Top + 8f, uIBox.Right - 12f, uIBox.Bottom - num - 14f), handle: handle, font: _nodeFont, lines: titleLines, color: TextPrimaryColor, scale: titleScale, lineHeightMultiplier: 0.82f);
			handle.DrawString(pos: new Vector2(uIBox.Right - dimensions.X - 10f, uIBox.Bottom - dimensions.Y - 8f), font: _chipFont, str: chipText.AsSpan(), scale: chipScale, color: tuple.Item3);
			handle.DrawString(pos: new Vector2(uIBox.Left + 12f, uIBox.Bottom - dimensions2.Y - 8f), font: _chipFont, str: footerText.AsSpan(), scale: chipScale, color: (nodeVisualState == NodeVisualState.LockedByPoints) ? CardDeniedColor : TextMutedColor);
		}
	}

	private static void DrawConnector(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color, bool leftSide, bool emphasize)
	{
		float num = (leftSide ? (-34f) : 34f);
		Vector2 value = new Vector2(start.X + num, start.Y);
		Vector2 value2 = new Vector2(end.X - num, end.Y);
		if (MathF.Abs(value.X - value2.X) < 20f)
		{
			Vector2 vector = (start + end) * 0.5f;
			value = new Vector2(vector.X, start.Y);
			value2 = new Vector2(vector.X, end.Y);
		}
		handle.DrawLine(Snap(start), Snap(value), color);
		handle.DrawLine(Snap(value), Snap(value2), color.WithAlpha(emphasize ? color.A : (color.A * 0.88f)));
		handle.DrawLine(Snap(value2), Snap(end), color);
	}

	private (Color Background, Color Border, Color Chip) ResolveNodeColors(Color accent, NodeVisualState state, bool hovered, float acceptPulse, float denyPulse, bool root)
	{
		Color color = (root ? CardBackgroundColor : CardMutedBackgroundColor);
		Color color2 = CardLockedBorderColor;
		Color item = TextMutedColor;
		switch (state)
		{
		case NodeVisualState.Opened:
			color = Blend(CardBackgroundColor, accent.WithAlpha(0.34f), 0.78f);
			color2 = accent.WithAlpha(0.92f);
			item = accent.WithAlpha(0.9f);
			break;
		case NodeVisualState.Planned:
			color = Blend(CardBackgroundColor, accent.WithAlpha(0.38f), 0.66f);
			color2 = accent.WithAlpha(0.95f);
			item = accent.WithAlpha(0.95f);
			break;
		case NodeVisualState.Available:
			color = Blend(CardBackgroundColor, accent.WithAlpha(0.2f), hovered ? 0.68f : 0.42f);
			color2 = accent.WithAlpha(hovered ? 0.95f : 0.74f);
			item = RootConnectorColor;
			break;
		case NodeVisualState.LockedByPoints:
			color = Blend(CardMutedBackgroundColor, CardDeniedColor.WithAlpha(0.18f), 0.48f);
			color2 = CardDeniedColor.WithAlpha(0.82f);
			item = CardDeniedColor.WithAlpha(0.95f);
			break;
		case NodeVisualState.LockedByParent:
			color = CardMutedBackgroundColor;
			color2 = (hovered ? CardLockedBorderColor.WithAlpha(0.92f) : CardLockedBorderColor.WithAlpha(0.65f));
			item = TextMutedColor;
			break;
		}
		if (hovered && state != NodeVisualState.Planned && state != NodeVisualState.Opened)
		{
			color = Blend(color, accent.WithAlpha(0.22f), 0.35f);
		}
		if (acceptPulse > 0f)
		{
			color2 = Blend(color2, accent.WithAlpha(1f), acceptPulse * 0.65f);
		}
		if (denyPulse > 0f)
		{
			color2 = Blend(color2, CardDeniedColor.WithAlpha(1f), denyPulse * 0.7f);
		}
		return (Background: color, Border: color2, Chip: item);
	}

	private NodeVisualState ResolveNodeState(NodeDefinition node)
	{
		if (_openedNodes.Contains(node.Id))
		{
			return NodeVisualState.Opened;
		}
		if (_plannedNodes.Contains(node.Id) || _submittedNodes.Contains(node.Id))
		{
			return NodeVisualState.Planned;
		}
		if (node.ParentId != null && !_openedNodes.Contains(node.ParentId) && !_plannedNodes.Contains(node.ParentId) && !_submittedNodes.Contains(node.ParentId))
		{
			return NodeVisualState.LockedByParent;
		}
		if (Math.Max(0, TotalSkillPoints - OpenedCost - PlannedCost - SubmittedCost) < node.Cost)
		{
			return NodeVisualState.LockedByPoints;
		}
		return NodeVisualState.Available;
	}

	private void TryActivateNode(Vector2 localPosition)
	{
		UpdateScreenLayout(new Vector2(base.PixelWidth, base.PixelHeight));
		foreach (NodeDefinition value2 in _nodes.Values)
		{
			if (_nodeBoxes.TryGetValue(value2.Id, out var value) && value.Contains(localPosition))
			{
				ToggleNode(value2);
				break;
			}
		}
	}

	private void ToggleNode(NodeDefinition node)
	{
		if (_openedNodes.Contains(node.Id))
		{
			RefreshHoverPresentation();
		}
		else if (_submittedNodes.Contains(node.Id))
		{
			RefreshHoverPresentation();
		}
		else if (_plannedNodes.Contains(node.Id))
		{
			RemovePlannedNodeRecursive(node.Id);
			_acceptPulses[node.Id] = 1f;
			RefreshHoverPresentation();
			this.PlannerChanged?.Invoke();
		}
		else if (ResolveNodeState(node) != NodeVisualState.Available)
		{
			_denyPulses[node.Id] = 1f;
			RefreshHoverPresentation();
		}
		else
		{
			_plannedNodes.Add(node.Id);
			_acceptPulses[node.Id] = 1f;
			RefreshHoverPresentation();
			this.PlannerChanged?.Invoke();
		}
	}

	private void RemovePlannedNodeRecursive(string nodeId)
	{
		if (_childrenByParent.TryGetValue(nodeId, out var value) && value != null)
		{
			foreach (string item in value)
			{
				if (_plannedNodes.Contains(item))
				{
					RemovePlannedNodeRecursive(item);
				}
			}
		}
		_plannedNodes.Remove(nodeId);
	}

	private void ClearPlanInternal(bool emitEvent)
	{
		_plannedNodes.Clear();
		_acceptPulses.Clear();
		_denyPulses.Clear();
		RefreshHoverPresentation();
		if (emitEvent)
		{
			this.PlannerChanged?.Invoke();
		}
	}

	private static bool AdvancePulseMap(Dictionary<string, float> map, float decay)
	{
		if (map.Count == 0)
		{
			return false;
		}
		bool flag = false;
		List<string> list = new List<string>();
		KeyValuePair<string, float>[] array = map.ToArray();
		foreach (KeyValuePair<string, float> keyValuePair in array)
		{
			keyValuePair.Deconstruct(out var key, out var value);
			string text = key;
			float num = value;
			float num2 = MathF.Max(0f, num - decay);
			if (!MathHelper.CloseTo(num, num2))
			{
				flag = true;
			}
			if (num2 <= 0f)
			{
				list.Add(text);
			}
			else
			{
				map[text] = num2;
			}
		}
		foreach (string item in list)
		{
			map.Remove(item);
		}
		if (!flag)
		{
			return list.Count > 0;
		}
		return true;
	}

	private void UpdateHoverFromMousePosition()
	{
		Vector2 localPosition = (base.UserInterfaceManager.MousePositionScaled.Position - base.GlobalPosition) * UIScale;
		if (localPosition.X < 0f || localPosition.Y < 0f || localPosition.X > (float)base.PixelWidth || localPosition.Y > (float)base.PixelHeight)
		{
			SetHoveredNode(null);
		}
		else
		{
			UpdateHoveredNode(localPosition);
		}
	}

	private void UpdateHoveredNode(Vector2 localPosition)
	{
		if (_dragging)
		{
			SetHoveredNode(null);
			return;
		}
		UpdateScreenLayout(new Vector2(base.PixelWidth, base.PixelHeight));
		foreach (NodeDefinition item in _nodes.Values.Reverse())
		{
			if (_nodeBoxes.TryGetValue(item.Id, out var value) && value.Contains(localPosition))
			{
				SetHoveredNode(item.Id);
				return;
			}
		}
		SetHoveredNode(null);
	}

	private void SetHoveredNode(string? nodeId)
	{
		if (!(_hoveredNodeId == nodeId))
		{
			_hoveredNodeId = nodeId;
			RefreshHoverPresentation();
		}
	}

	private void RefreshHoverPresentation()
	{
		WH40KCharacterDevelopmentNodePresentation? currentHoverInfo = CurrentHoverInfo;
		if (_hoveredNodeId == null || !_nodes.TryGetValue(_hoveredNodeId, out var value) || value == null)
		{
			CurrentHoverInfo = null;
			_dollView.SetActiveOrgan(null, Color.White);
			if (currentHoverInfo != null)
			{
				this.HoverChanged?.Invoke(null);
			}
			return;
		}
		BranchDefinition branchDefinition = _branches[value.BranchId];
		NodeVisualState nodeVisualState = ResolveNodeState(value);
		WH40KCharacterDevelopmentNodePresentation wH40KCharacterDevelopmentNodePresentation = (CurrentHoverInfo = new WH40KCharacterDevelopmentNodePresentation(
			branchDefinition.TitleKey,
			branchDefinition.SubtitleKey,
			value.TitleKey,
			value.DescriptionKey,
			BuildStateText(value, nodeVisualState),
			BuildDescriptionSupplement(value, nodeVisualState),
			value.Cost,
			nodeVisualState == NodeVisualState.Planned,
			nodeVisualState == NodeVisualState.Available || nodeVisualState == NodeVisualState.Planned || nodeVisualState == NodeVisualState.Opened,
			branchDefinition.Organ,
			branchDefinition.Accent));
		_dollView.SetActiveOrgan(branchDefinition.Organ, branchDefinition.Accent);
		if (!object.Equals(currentHoverInfo, wH40KCharacterDevelopmentNodePresentation))
		{
			this.HoverChanged?.Invoke(wH40KCharacterDevelopmentNodePresentation);
		}
	}

	private string BuildStateText(NodeDefinition node, NodeVisualState state)
	{
		return state switch
		{
			NodeVisualState.Opened => Loc.GetString("w40k-cd-state-opened"),
			NodeVisualState.Planned => Loc.GetString("w40k-cd-state-planned"),
			NodeVisualState.Available => Loc.GetString("w40k-cd-state-available"),
			NodeVisualState.LockedByPoints => Loc.GetString(
				"w40k-cd-state-locked-points-detail",
				("missing", GetMissingPoints(node))),
			_ => Loc.GetString(
				"w40k-cd-state-locked-chain-detail",
				("parent", GetParentTitle(node))),
		};
	}

	private string? BuildDescriptionSupplement(NodeDefinition node, NodeVisualState state)
	{
		return state switch
		{
			NodeVisualState.Opened => Loc.GetString("w40k-cd-note-opened"),
			NodeVisualState.Planned => Loc.GetString("w40k-cd-note-planned"),
			NodeVisualState.Available => Loc.GetString("w40k-cd-note-available"),
			NodeVisualState.LockedByPoints => Loc.GetString(
				"w40k-cd-note-locked-points",
				("missing", GetMissingPoints(node))),
			_ => Loc.GetString(
				"w40k-cd-note-locked-parent",
				("parent", GetParentTitle(node))),
		};
	}

	private string GetParentTitle(NodeDefinition node)
	{
		if (node.ParentId == null)
			return Loc.GetString("w40k-cd-default-node");

		return _nodeTitles.GetValueOrDefault(node.ParentId, node.ParentId);
	}

	private int GetMissingPoints(NodeDefinition node)
	{
		var remainingPoints = Math.Max(0, TotalSkillPoints - OpenedCost - PlannedCost - SubmittedCost);
		return Math.Max(0, node.Cost - remainingPoints);
	}

	private Vector2 ComputeRootPosition(bool leftSide, BranchLane lane, Vector2 maxRootSize, Vector2 maxNodeSize)
	{
		float branchRowOffsetY = GetBranchRowOffsetY(maxNodeSize);
		float num = DollSize.X * 0.5f + maxRootSize.X * 0.5f + GetNominalBranchHeaderWorldSize(maxNodeSize).X * 0.45f;
		float num2 = branchRowOffsetY * 2f + maxNodeSize.Y + 10f;
		float y = lane switch
		{
			BranchLane.Top => 0f - num2,
			BranchLane.Middle => 0f,
			BranchLane.Bottom => num2,
			_ => 0f,
		};
		return new Vector2(leftSide ? (0f - num) : num, y);
	}

	private void RecalculateNodeWorldSizes()
	{
		_nodeWorldSizes.Clear();
		foreach (NodeDefinition value in _nodes.Values)
		{
			_nodeWorldSizes[value.Id] = ResolveNodeWorldSize(value);
		}
	}

	private void RecalculateBranchWorldLayout()
	{
		if (_branches.Count == 0)
		{
			return;
		}
		Vector2 maxNodeWorldSize = GetMaxNodeWorldSize(root: true);
		Vector2 maxNodeWorldSize2 = GetMaxNodeWorldSize(root: false);
		float branchRowOffsetY = GetBranchRowOffsetY(maxNodeWorldSize2);
		foreach (BranchDefinition value in _branches.Values)
		{
			Vector2 vector = (value.RootPosition = ComputeRootPosition(value.LeftSide, value.Lane, maxNodeWorldSize, maxNodeWorldSize2));
			_nodes[value.RootNodeId].WorldPosition = vector;
			LayoutBranchRow(value, value.UpperPath, vector, 0f - branchRowOffsetY);
			LayoutBranchRow(value, value.LowerPath, vector, branchRowOffsetY);
		}
	}

	private void LayoutBranchRow(BranchDefinition branch, IReadOnlyList<string> rowIds, Vector2 rootPosition, float rowOffsetY)
	{
		float num = (branch.LeftSide ? (-1f) : 1f);
		float num2 = _nodeWorldSizes.GetValueOrDefault(branch.RootNodeId, RootNodeSize).X * 0.5f;
		foreach (string item in rowIds.Where((string id) => !string.IsNullOrWhiteSpace(id) && _nodes.ContainsKey(id)))
		{
			NodeDefinition nodeDefinition = _nodes[item];
			Vector2 valueOrDefault = _nodeWorldSizes.GetValueOrDefault(item, nodeDefinition.IsRoot ? RootNodeSize : NodeSize);
			num2 += 78f + valueOrDefault.X * 0.5f;
			nodeDefinition.WorldPosition = new Vector2(rootPosition.X + num * num2, rootPosition.Y + rowOffsetY);
			num2 += valueOrDefault.X * 0.5f;
		}
	}

	private Vector2 GetMaxNodeWorldSize(bool root)
	{
		Vector2[] array = (from node in _nodes.Values
			where node.IsRoot == root
			select _nodeWorldSizes.GetValueOrDefault(node.Id, root ? RootNodeSize : NodeSize)).ToArray();
		if (array.Length == 0)
		{
			if (!root)
			{
				return NodeSize;
			}
			return RootNodeSize;
		}
		return new Vector2(array.Max((Vector2 size) => size.X), array.Max((Vector2 size) => size.Y));
	}

	private static float GetBranchRowOffsetY(Vector2 maxNodeSize)
	{
		return maxNodeSize.Y + 92f;
	}

	private void RecalculateContentWorldBounds()
	{
		if (_nodes.Count == 0)
		{
			_contentWorldBounds = UIBox2.FromDimensions(-1f, -1f, 2f, 2f);
			_contentWorldCenter = Vector2.Zero;
			return;
		}
		float left = float.MaxValue;
		float top = float.MaxValue;
		float right = float.MinValue;
		float bottom = float.MinValue;
		foreach (NodeDefinition value in _nodes.Values)
		{
			UIBox2 box = BoxFromCenter(value.WorldPosition, _nodeWorldSizes.GetValueOrDefault(value.Id, value.IsRoot ? RootNodeSize : NodeSize));
			IncludeBox(ref left, ref top, ref right, ref bottom, box);
		}
		foreach (BranchDefinition value2 in _branches.Values)
		{
			UIBox2 box2 = BoxFromCenter(GetBranchHeaderCenterWorld(value2), GetBranchHeaderWorldSize(value2));
			IncludeBox(ref left, ref top, ref right, ref bottom, box2);
		}
		IncludeBox(ref left, ref top, ref right, ref bottom, BoxFromCenter(Vector2.Zero, DollSize));
		_contentWorldBounds = ExpandBox(new UIBox2(left, top, right, bottom), 120f);
		_contentWorldCenter = (_contentWorldBounds.TopLeft + _contentWorldBounds.BottomRight) * 0.5f;
	}

	private void ResolveBranchTypography(DrawingHandleScreen handle, BranchDefinition branch, UIBox2 headerBox, float scale, out string[] titleLines, out string subtitle, out float titleScale, out float subtitleScale)
	{
		string valueOrDefault = _branchTitles.GetValueOrDefault(branch.Id, branch.TitleKey);
		string source = (subtitle = _branchSubtitles.GetValueOrDefault(branch.Id, branch.SubtitleKey));
		float maxWidth = headerBox.Width - 28f;
		titleScale = Math.Clamp(scale * 1.08f, 0.34f, 1.05f);
		subtitleScale = Math.Clamp(titleScale * 0.78f, 0.26f, 0.94f);
		titleLines = Array.Empty<string>();
		for (int i = 0; i < 12; i++)
		{
			titleLines = WrapText(handle, _headerFont, valueOrDefault, maxWidth, 2, titleScale);
			subtitle = FitText(handle, _nodeFont, source, maxWidth, subtitleScale);
			float num = MeasureLinesHeight(handle, _headerFont, titleLines, titleScale, 0.86f);
			float y = handle.GetDimensions(_nodeFont, subtitle.AsSpan(), subtitleScale).Y;
			if (num + y + 26f <= headerBox.Height)
			{
				return;
			}
			titleScale *= 0.9f;
			subtitleScale *= 0.9f;
		}
		titleLines = WrapText(handle, _headerFont, valueOrDefault, maxWidth, 2, titleScale);
		subtitle = FitText(handle, _nodeFont, source, maxWidth, subtitleScale);
	}

	private void ResolveNodeTypography(DrawingHandleScreen handle, NodeDefinition node, UIBox2 box, float scale, out string[] titleLines, out string footerText, out string chipText, out float titleScale, out float chipScale)
	{
		string valueOrDefault = _nodeTitles.GetValueOrDefault(node.Id, node.TitleKey);
		footerText = _nodeCostLabel;
		chipText = _nodeCostChips.GetValueOrDefault(node.Id, node.Cost.ToString());
		titleScale = Math.Clamp(scale * (node.IsRoot ? 1.18f : 1.08f), 0.28f, 1.02f);
		chipScale = Math.Clamp(titleScale * 0.78f, 0.24f, 0.92f);
		titleLines = Array.Empty<string>();
		float maxWidth = box.Width - 24f;
		for (int i = 0; i < 14; i++)
		{
			titleLines = WrapText(handle, _nodeFont, valueOrDefault, maxWidth, 2, titleScale);
			Vector2 dimensions = handle.GetDimensions(_chipFont, chipText.AsSpan(), chipScale);
			Vector2 dimensions2 = handle.GetDimensions(_chipFont, footerText.AsSpan(), chipScale);
			float num = MathF.Max(dimensions.Y, dimensions2.Y);
			bool num2 = dimensions2.X + dimensions.X + 18f <= box.Width - 20f;
			float num3 = box.Height - num - 22f;
			bool flag = MeasureLinesHeight(handle, _nodeFont, titleLines, titleScale, 0.82f) <= num3;
			if (num2 && flag)
			{
				return;
			}
			titleScale *= 0.9f;
			chipScale *= 0.88f;
		}
		titleLines = WrapText(handle, _nodeFont, valueOrDefault, maxWidth, 2, titleScale);
	}

	private Vector2 TransformWorld(Vector2 worldPosition)
	{
		return _canvasCenter + (worldPosition - _contentWorldCenter) * (_fitScale * _zoom);
	}

	private Vector2 TransformOrgan(WH40KCharacterDevelopmentOrganType organ)
	{
		Vector2 organAnchorFraction = _dollView.GetOrganAnchorFraction(organ);
		return _dollBox.TopLeft + (_dollBox.BottomRight - _dollBox.TopLeft) * organAnchorFraction;
	}

	private Vector2 GetBranchHeaderCenter(BranchDefinition branch)
	{
		string[] array = (from id in branch.UpperPath.Concat(branch.LowerPath)
			where !string.IsNullOrWhiteSpace(id)
			select id).ToArray();
		if (array.Length == 0 || array.Any((string id) => !_nodeBoxes.ContainsKey(id)))
		{
			return TransformWorld(branch.RootPosition);
		}
		float num = float.MaxValue;
		float num2 = float.MaxValue;
		float num3 = float.MinValue;
		float num4 = float.MinValue;
		string[] array2 = array;
		foreach (string key in array2)
		{
			UIBox2 uIBox = _nodeBoxes[key];
			num = MathF.Min(num, uIBox.Left);
			num2 = MathF.Min(num2, uIBox.Top);
			num3 = MathF.Max(num3, uIBox.Right);
			num4 = MathF.Max(num4, uIBox.Bottom);
		}
		return new Vector2((num + num3) * 0.5f, (num2 + num4) * 0.5f);
	}

	private Vector2 GetBranchHeaderCenterWorld(BranchDefinition branch)
	{
		string[] array = (from id in branch.UpperPath.Concat(branch.LowerPath)
			where !string.IsNullOrWhiteSpace(id)
			select id).ToArray();
		if (array.Length == 0 || array.Any((string id) => !_nodes.ContainsKey(id)))
		{
			return branch.RootPosition;
		}
		float left = float.MaxValue;
		float top = float.MaxValue;
		float right = float.MinValue;
		float bottom = float.MinValue;
		string[] array2 = array;
		foreach (string key in array2)
		{
			NodeDefinition nodeDefinition = _nodes[key];
			UIBox2 box = BoxFromCenter(nodeDefinition.WorldPosition, _nodeWorldSizes.GetValueOrDefault(key, nodeDefinition.IsRoot ? RootNodeSize : NodeSize));
			IncludeBox(ref left, ref top, ref right, ref bottom, box);
		}
		return new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f);
	}

	private Vector2 GetBranchHeaderWorldSize(BranchDefinition branch)
	{
		Vector2 nominalBranchHeaderWorldSize = GetNominalBranchHeaderWorldSize();
		string[] array = branch.UpperPath.Where((string id) => !string.IsNullOrWhiteSpace(id) && _nodes.ContainsKey(id)).ToArray();
		string[] array2 = branch.LowerPath.Where((string id) => !string.IsNullOrWhiteSpace(id) && _nodes.ContainsKey(id)).ToArray();
		if (array.Length == 0 || array2.Length == 0)
		{
			return nominalBranchHeaderWorldSize;
		}
		float num = float.MaxValue;
		float num2 = float.MinValue;
		foreach (string item in array.Concat(array2))
		{
			NodeDefinition nodeDefinition = _nodes[item];
			UIBox2 uIBox = BoxFromCenter(nodeDefinition.WorldPosition, _nodeWorldSizes.GetValueOrDefault(item, nodeDefinition.IsRoot ? RootNodeSize : NodeSize));
			num = MathF.Min(num, uIBox.Left);
			num2 = MathF.Max(num2, uIBox.Right);
		}
		float num3 = float.MinValue;
		string[] array3 = array;
		foreach (string key in array3)
		{
			NodeDefinition nodeDefinition2 = _nodes[key];
			num3 = MathF.Max(num3, BoxFromCenter(nodeDefinition2.WorldPosition, _nodeWorldSizes.GetValueOrDefault(key, nodeDefinition2.IsRoot ? RootNodeSize : NodeSize)).Bottom);
		}
		float num5 = float.MaxValue;
		array3 = array2;
		foreach (string key2 in array3)
		{
			NodeDefinition nodeDefinition3 = _nodes[key2];
			num5 = MathF.Min(num5, BoxFromCenter(nodeDefinition3.WorldPosition, _nodeWorldSizes.GetValueOrDefault(key2, nodeDefinition3.IsRoot ? RootNodeSize : NodeSize)).Top);
		}
		Vector2 maxNodeWorldSize = GetMaxNodeWorldSize(root: false);
		float x = MathF.Max(nominalBranchHeaderWorldSize.X, (num2 - num) * 0.58f);
		float num6 = MathF.Max(maxNodeWorldSize.Y, num5 - num3);
		float y = MathF.Max(nominalBranchHeaderWorldSize.Y, num6 * 0.5f);
		return new Vector2(x, y);
	}

	private Vector2 GetNominalBranchHeaderWorldSize()
	{
		return GetNominalBranchHeaderWorldSize(GetMaxNodeWorldSize(root: false));
	}

	private Vector2 GetNominalBranchHeaderWorldSize(Vector2 maxNodeSize)
	{
		float num = maxNodeSize.X * 3f + 156f;
		float num2 = GetBranchRowOffsetY(maxNodeSize) * 2f - maxNodeSize.Y;
		return new Vector2(num * 0.58f, num2 * 0.5f);
	}

	private Vector2 ResolveNodeWorldSize(NodeDefinition node)
	{
		Vector2 vector = (node.IsRoot ? RootNodeSize : NodeSize);
		float x = (node.IsRoot ? 390f : 340f);
		string valueOrDefault = _nodeTitles.GetValueOrDefault(node.Id, node.TitleKey);
		string nodeCostLabel = _nodeCostLabel;
		string valueOrDefault2 = _nodeCostChips.GetValueOrDefault(node.Id, node.Cost.ToString());
		float scale = (node.IsRoot ? 1.1f : 1.02f);
		float num = vector.X;
		for (int i = 0; i < 16; i++)
		{
			string[] lines = WrapText(_nodeFont, valueOrDefault, num - 24f, 2, scale);
			float num2 = MeasureLinesHeight(_nodeFont, lines, scale, 0.82f);
			float num3 = MeasureText(_chipFont, nodeCostLabel, 0.82f).X + MeasureText(_chipFont, valueOrDefault2, 0.82f).X + 18f;
			float num4 = MathF.Max(MeasureText(_chipFont, nodeCostLabel, 0.82f).Y, MeasureText(_chipFont, valueOrDefault2, 0.82f).Y);
			bool num5 = num2 <= vector.Y - num4 - 22f;
			bool flag = num3 <= num - 20f;
			if (num5 && flag)
			{
				break;
			}
			num = MathF.Min(x, num + 18f);
		}
		string[] lines2 = WrapText(_nodeFont, valueOrDefault, num - 24f, 2, scale);
		float num6 = MeasureLinesHeight(_nodeFont, lines2, scale, 0.82f);
		float num7 = MathF.Max(MeasureText(_chipFont, nodeCostLabel, 0.82f).Y, MeasureText(_chipFont, valueOrDefault2, 0.82f).Y);
		float y = MathF.Max(vector.Y, num6 + num7 + 22f);
		return new Vector2(num, y);
	}

	private Vector2 GetPathAnchor(string nodeId, bool leftSide, bool entrance)
	{
		UIBox2 uIBox = _nodeBoxes[nodeId];
		return new Vector2((!leftSide) ? (entrance ? uIBox.Left : uIBox.Right) : (entrance ? uIBox.Right : uIBox.Left), uIBox.Center.Y);
	}

	private static UIBox2 BoxFromCenter(Vector2 center, Vector2 size)
	{
		Vector2 vector = size * 0.5f;
		return new UIBox2(center - vector, center + vector);
	}

	private static UIBox2 ExpandBox(UIBox2 box, float padding)
	{
		return new UIBox2(box.Left - padding, box.Top - padding, box.Right + padding, box.Bottom + padding);
	}

	private static void IncludeBox(ref float left, ref float top, ref float right, ref float bottom, UIBox2 box)
	{
		left = MathF.Min(left, box.Left);
		top = MathF.Min(top, box.Top);
		right = MathF.Max(right, box.Right);
		bottom = MathF.Max(bottom, box.Bottom);
	}

	private static Vector2 Snap(Vector2 value)
	{
		return new Vector2(MathF.Round(value.X), MathF.Round(value.Y));
	}

	private static bool ApproximatelyEqual(Vector2 a, Vector2 b, float epsilon = 0.5f)
	{
		if (MathF.Abs(a.X - b.X) <= epsilon)
		{
			return MathF.Abs(a.Y - b.Y) <= epsilon;
		}
		return false;
	}

	private static Color Blend(Color from, Color to, float amount)
	{
		float blend = MathHelper.Clamp01(amount);
		return new Color(MathHelper.Lerp(from.R, to.R, blend), MathHelper.Lerp(from.G, to.G, blend), MathHelper.Lerp(from.B, to.B, blend), MathHelper.Lerp(from.A, to.A, blend));
	}

	private static string FitText(DrawingHandleScreen handle, Font font, string source, float maxWidth, float scale = 1f)
	{
		return FitText(font, source, maxWidth, scale);
	}

	private static string FitText(Font font, string source, float maxWidth, float scale = 1f)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return string.Empty;
		}
		if (MeasureText(font, source, scale).X <= maxWidth)
		{
			return source;
		}
		string text = source.Trim();
		while (text.Length > 1)
		{
			string text2 = text;
			text = text2.Substring(0, text2.Length - 1);
			string text3 = text + "...";
			if (MeasureText(font, text3, scale).X <= maxWidth)
			{
				return text3;
			}
		}
		return "...";
	}

	private static string[] WrapText(DrawingHandleScreen handle, Font font, string source, float maxWidth, int maxLines, float scale = 1f)
	{
		return WrapText(font, source, maxWidth, maxLines, scale);
	}

	private static string[] WrapText(Font font, string source, float maxWidth, int maxLines, float scale = 1f)
	{
		if (string.IsNullOrWhiteSpace(source) || maxLines <= 0)
		{
			return Array.Empty<string>();
		}
		string[] array = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			return Array.Empty<string>();
		}
		List<string> list = new List<string>(maxLines);
		string text = array[0];
		for (int i = 1; i < array.Length; i++)
		{
			string text2 = text + " " + array[i];
			if (MeasureText(font, text2, scale).X <= maxWidth)
			{
				text = text2;
				continue;
			}
			list.Add(FitText(font, text, maxWidth, scale));
			text = array[i];
			if (list.Count == maxLines - 1)
			{
				string source2 = string.Join(' ', array.Skip(i));
				list.Add(FitText(font, source2, maxWidth, scale));
				return list.ToArray();
			}
		}
		list.Add(FitText(font, text, maxWidth, scale));
		return list.Take(maxLines).ToArray();
	}

	private static float MeasureLinesHeight(DrawingHandleScreen handle, Font font, IReadOnlyList<string> lines, float scale, float lineHeightMultiplier)
	{
		if (lines.Count == 0)
		{
			return 0f;
		}
		float y = MeasureText(font, "Ag", scale).Y;
		float num = y * lineHeightMultiplier;
		return y + (float)(lines.Count - 1) * num;
	}

	private static float MeasureLinesHeight(Font font, IReadOnlyList<string> lines, float scale, float lineHeightMultiplier)
	{
		if (lines.Count == 0)
		{
			return 0f;
		}
		float y = MeasureText(font, "Ag", scale).Y;
		float num = y * lineHeightMultiplier;
		return y + (float)(lines.Count - 1) * num;
	}

	private static Vector2 MeasureText(Font font, string source, float scale)
	{
		if (string.IsNullOrEmpty(source))
		{
			return Vector2.Zero;
		}
		float num = 0f;
		foreach (Rune item in source.EnumerateRunes())
		{
			if (font.TryGetCharMetrics(item, scale, out var metrics))
			{
				num += (float)metrics.Advance;
			}
		}
		return new Vector2(num, font.GetLineHeight(scale));
	}

	private static void DrawLinesInBox(DrawingHandleScreen handle, Font font, IReadOnlyList<string> lines, UIBox2 box, Color color, float scale, float lineHeightMultiplier)
	{
		if (lines.Count != 0)
		{
			float num = handle.GetDimensions(font, "Ag".AsSpan(), scale).Y * lineHeightMultiplier;
			float num2 = MeasureLinesHeight(handle, font, lines, scale, lineHeightMultiplier);
			Vector2 vector = new Vector2(box.Left, box.Top + MathF.Max(0f, (box.Height - num2) * 0.5f));
			for (int i = 0; i < lines.Count; i++)
			{
				handle.DrawString(font, vector + new Vector2(0f, num * (float)i), lines[i].AsSpan(), scale, color);
			}
		}
	}
}
