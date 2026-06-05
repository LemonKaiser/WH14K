using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Lobby.UI.ProfileEditorControls;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client.Lobby.UI;

public sealed class LobbyCharacterCarousel : LayoutContainer
{
    private static readonly Vector2 StageSize = new(316f, 178f);
    private static readonly Vector2 PreviewBounds = new(128f, 160f);
    private const float TransitionDuration = 0.28f;

    private static readonly Color CenterTint = Color.White;
    private static readonly Color SideTint = new(0.34f, 0.34f, 0.37f, 0.96f);
    private static readonly Color HiddenTint = new(1f, 1f, 1f, 0f);

    private readonly PreviewNode[] _nodes = new PreviewNode[5];
    private readonly CarouselArrowControl _leftArrow;
    private readonly CarouselArrowControl _rightArrow;
    private readonly List<CharacterEntry> _entries = new();
    private readonly AnimationState[] _animationStates = new AnimationState[5];

    private int _centerIndex;
    private int _targetCenterIndex;
    private int _direction;
    private float _transitionProgress;
    private bool _isAnimating;

    public bool HasCharacters => _entries.Count > 0;
    public event Action<int>? CharacterSelected;

    public LobbyCharacterCarousel()
    {
        MinSize = StageSize;
        SetSize = StageSize;
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;

        _leftArrow = new CarouselArrowControl(pointsLeft: true);
        _leftArrow.Pressed += () => TryNavigate(-1);
        AddChild(_leftArrow);

        _rightArrow = new CarouselArrowControl(pointsLeft: false);
        _rightArrow.Pressed += () => TryNavigate(1);
        AddChild(_rightArrow);

        for (var i = 0; i < _nodes.Length; i++)
        {
            var node = new PreviewNode();
            node.Pressed += TryNavigate;

            _nodes[i] = node;
            AddChild(node);
        }

        UpdateStaticLayout();
        SyncNodesToCurrentCenter();
    }

    public void SetCharacters(PlayerPreferences? preferences)
    {
        _entries.Clear();
        _isAnimating = false;
        _transitionProgress = 0f;

        if (preferences != null)
        {
            foreach (var (slot, profile) in preferences.Characters.OrderBy(character => character.Key))
            {
                _entries.Add(new CharacterEntry(slot, profile));
            }
        }

        if (_entries.Count > 0)
        {
            var selectedIndex = _entries.FindIndex(entry => entry.Slot == preferences?.SelectedCharacterIndex);
            _centerIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }
        else
        {
            _centerIndex = 0;
        }

        SyncNodesToCurrentCenter();
    }

    public void Clear()
    {
        _entries.Clear();
        _centerIndex = 0;
        _isAnimating = false;
        _transitionProgress = 0f;
        SyncNodesToCurrentCenter();
    }

    public void SetSelectedSlot(int? slot)
    {
        if (_entries.Count == 0 || slot == null)
            return;

        var selectedIndex = _entries.FindIndex(entry => entry.Slot == slot.Value);
        if (selectedIndex < 0)
            return;

        _isAnimating = false;
        _transitionProgress = 0f;
        _direction = 0;
        _centerIndex = selectedIndex;
        SyncNodesToCurrentCenter();
    }

    protected override void Resized()
    {
        base.Resized();
        UpdateStaticLayout();

        if (_isAnimating)
            ApplyAnimationFrame(Easings.InOutCubic(_transitionProgress));
        else
            SyncNodesToCurrentCenter();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_isAnimating)
            return;

        _transitionProgress = Math.Clamp(_transitionProgress + args.DeltaSeconds / TransitionDuration, 0f, 1f);
        ApplyAnimationFrame(Easings.InOutCubic(_transitionProgress));

        if (_transitionProgress < 1f)
            return;

        _centerIndex = _targetCenterIndex;
        RotateNodesAfterAnimation(_direction);
        _direction = 0;
        _isAnimating = false;
        _transitionProgress = 0f;
        SyncNodesToCurrentCenter();

        if (CurrentEntry is { } current)
            CharacterSelected?.Invoke(current.Slot);
    }

    private void TryNavigate(int direction)
    {
        if (_isAnimating || !CanNavigate(direction))
            return;

        _direction = Math.Sign(direction);
        _targetCenterIndex = ResolveCenterIndexAfterMove(_direction);
        _transitionProgress = 0f;
        _isAnimating = true;
        BuildAnimationStates(_direction);
        UpdateArrowState();
        ApplyAnimationFrame(0f);
    }

    private void BuildAnimationStates(int direction)
    {
        for (var i = 0; i < _animationStates.Length; i++)
        {
            var sourceRole = (CarouselRole) i;
            var targetRole = sourceRole;
            var shouldAnimate = true;

            if (direction > 0)
            {
                targetRole = sourceRole switch
                {
                    CarouselRole.Left => CarouselRole.FarLeft,
                    CarouselRole.Center => CarouselRole.Left,
                    CarouselRole.Right => CarouselRole.Center,
                    CarouselRole.FarRight => CarouselRole.Right,
                    CarouselRole.FarLeft => CarouselRole.FarLeft,
                    _ => sourceRole
                };

                shouldAnimate = sourceRole != CarouselRole.FarLeft;
            }
            else if (direction < 0)
            {
                targetRole = sourceRole switch
                {
                    CarouselRole.Right => CarouselRole.FarRight,
                    CarouselRole.Center => CarouselRole.Right,
                    CarouselRole.Left => CarouselRole.Center,
                    CarouselRole.FarLeft => CarouselRole.Left,
                    CarouselRole.FarRight => CarouselRole.FarRight,
                    _ => sourceRole
                };

                shouldAnimate = sourceRole != CarouselRole.FarRight;
            }

            _animationStates[i] = new AnimationState(
                _nodes[i],
                ResolvePoseState(sourceRole, ResolveEntryIndexForRole(_centerIndex, sourceRole) != null, isAnimating: true),
                ResolvePoseState(targetRole, ResolveEntryIndexForRole(_targetCenterIndex, targetRole) != null, isAnimating: true),
                shouldAnimate);
        }
    }

    private void ApplyAnimationFrame(float progress)
    {
        for (var i = 0; i < _animationStates.Length; i++)
        {
            var animation = _animationStates[i];
            var pose = animation.ShouldAnimate
                ? PoseState.Lerp(animation.From, animation.To, progress)
                : animation.From;

            animation.Node.ApplyPose(pose, allowInteraction: false);
        }

        UpdateLayering();
    }

    private void SyncNodesToCurrentCenter()
    {
        for (var i = 0; i < _nodes.Length; i++)
        {
            var role = (CarouselRole) i;
            var entryIndex = ResolveEntryIndexForRole(_centerIndex, role);
            CharacterEntry? entry = null;

            if (entryIndex != null)
                entry = _entries[entryIndex.Value];

            _nodes[i].SetCharacter(entry?.Profile, entry?.Slot);
            _nodes[i].ApplyPose(
                ResolvePoseState(role, entry != null, isAnimating: false),
                allowInteraction: role switch
                {
                    CarouselRole.Left => entry != null && CanNavigate(-1),
                    CarouselRole.Right => entry != null && CanNavigate(1),
                    _ => false
                },
                navigateDirection: role switch
                {
                    CarouselRole.Left => -1,
                    CarouselRole.Right => 1,
                    _ => 0
                });
        }

        UpdateLayering();
        UpdateArrowState();
    }

    private void UpdateLayering()
    {
        _nodes[(int) CarouselRole.FarLeft].SetPositionInParent(0);
        _nodes[(int) CarouselRole.FarRight].SetPositionInParent(1);
        _nodes[(int) CarouselRole.Left].SetPositionInParent(2);
        _nodes[(int) CarouselRole.Right].SetPositionInParent(3);
        _nodes[(int) CarouselRole.Center].SetPositionInParent(4);
        _leftArrow.SetPositionInParent(5);
        _rightArrow.SetPositionInParent(6);
    }

    private void UpdateArrowState()
    {
        _leftArrow.SetState(CanNavigate(-1), _isAnimating);
        _rightArrow.SetState(CanNavigate(1), _isAnimating);
    }

    private void RotateNodesAfterAnimation(int direction)
    {
        if (direction > 0)
        {
            var farLeft = _nodes[(int) CarouselRole.FarLeft];
            var left = _nodes[(int) CarouselRole.Left];
            var center = _nodes[(int) CarouselRole.Center];
            var right = _nodes[(int) CarouselRole.Right];
            var farRight = _nodes[(int) CarouselRole.FarRight];

            _nodes[(int) CarouselRole.FarLeft] = left;
            _nodes[(int) CarouselRole.Left] = center;
            _nodes[(int) CarouselRole.Center] = right;
            _nodes[(int) CarouselRole.Right] = farRight;
            _nodes[(int) CarouselRole.FarRight] = farLeft;
            return;
        }

        if (direction < 0)
        {
            var farLeft = _nodes[(int) CarouselRole.FarLeft];
            var left = _nodes[(int) CarouselRole.Left];
            var center = _nodes[(int) CarouselRole.Center];
            var right = _nodes[(int) CarouselRole.Right];
            var farRight = _nodes[(int) CarouselRole.FarRight];

            _nodes[(int) CarouselRole.FarLeft] = farRight;
            _nodes[(int) CarouselRole.Left] = farLeft;
            _nodes[(int) CarouselRole.Center] = left;
            _nodes[(int) CarouselRole.Right] = center;
            _nodes[(int) CarouselRole.FarRight] = right;
        }
    }

    private void UpdateStaticLayout()
    {
        var width = Size.X > 0f ? Size.X : StageSize.X;

        _leftArrow.SetSize = new Vector2(24f, 58f);
        SetPosition(_leftArrow, new Vector2(8f, 60f));

        _rightArrow.SetSize = new Vector2(24f, 58f);
        SetPosition(_rightArrow, new Vector2(width - 32f, 60f));
    }

    private bool CanNavigate(int direction)
    {
        if (_entries.Count <= 1)
            return false;

        if (_entries.Count == 2)
        {
            return direction switch
            {
                < 0 => _centerIndex > 0,
                > 0 => _centerIndex < _entries.Count - 1,
                _ => false
            };
        }

        return true;
    }

    private int ResolveCenterIndexAfterMove(int direction)
    {
        if (_entries.Count <= 1 || direction == 0)
            return _centerIndex;

        if (_entries.Count == 2)
            return Math.Clamp(_centerIndex + direction, 0, _entries.Count - 1);

        return WrapIndex(_centerIndex + direction);
    }

    private int? ResolveEntryIndexForRole(int centerIndex, CarouselRole role)
    {
        if (_entries.Count == 0)
            return null;

        if (_entries.Count == 1)
            return role == CarouselRole.Center ? 0 : null;

        if (_entries.Count == 2)
        {
            return role switch
            {
                CarouselRole.Center => centerIndex,
                CarouselRole.Left when centerIndex > 0 => centerIndex - 1,
                CarouselRole.Right when centerIndex < _entries.Count - 1 => centerIndex + 1,
                _ => null
            };
        }

        return role switch
        {
            CarouselRole.Center => WrapIndex(centerIndex),
            CarouselRole.Left => WrapIndex(centerIndex - 1),
            CarouselRole.Right => WrapIndex(centerIndex + 1),
            CarouselRole.FarLeft => WrapIndex(centerIndex - 2),
            CarouselRole.FarRight => WrapIndex(centerIndex + 2),
            _ => null
        };
    }

    private PoseState ResolvePoseState(CarouselRole role, bool hasCharacter, bool isAnimating)
    {
        var pose = role switch
        {
            // Keep preview bounds fixed so the sprite stays anchored to one stable center while only
            // the world position, tint, and render scale animate.
            CarouselRole.FarLeft => new PoseState(new Vector2(-117f, 11f), PreviewBounds, 2.94f, HiddenTint),
            CarouselRole.Left => new PoseState(new Vector2(15f, 11f), PreviewBounds, 2.94f, SideTint),
            CarouselRole.Center => new PoseState(new Vector2(94f, 5f), PreviewBounds, 4.08f, CenterTint),
            CarouselRole.Right => new PoseState(new Vector2(173f, 11f), PreviewBounds, 2.94f, SideTint),
            CarouselRole.FarRight => new PoseState(new Vector2(305f, 11f), PreviewBounds, 2.94f, HiddenTint),
            _ => PoseState.Hidden
        };

        if (hasCharacter)
            return pose;

        return isAnimating
            ? pose with { Modulate = HiddenTint }
            : PoseState.HiddenAt(pose.Position, pose.Size, pose.Scale);
    }

    private int WrapIndex(int index)
    {
        if (_entries.Count == 0)
            return 0;

        var wrapped = index % _entries.Count;
        return wrapped < 0 ? wrapped + _entries.Count : wrapped;
    }

    private CharacterEntry? CurrentEntry => _entries.Count == 0 ? null : _entries[_centerIndex];

    private readonly record struct CharacterEntry(int Slot, HumanoidCharacterProfile Profile);

    private readonly record struct AnimationState(PreviewNode Node, PoseState From, PoseState To, bool ShouldAnimate);

    private enum CarouselRole
    {
        FarLeft,
        Left,
        Center,
        Right,
        FarRight
    }

    private readonly record struct PoseState(Vector2 Position, Vector2 Size, float Scale, Color Modulate)
    {
        public static PoseState Hidden => new(Vector2.Zero, Vector2.Zero, 1f, HiddenTint);

        public static PoseState HiddenAt(Vector2 position, Vector2 size, float scale)
        {
            return new PoseState(position, size, scale, HiddenTint);
        }

        public static PoseState Lerp(PoseState from, PoseState to, float amount)
        {
            return new PoseState(
                Snap(Vector2.Lerp(from.Position, to.Position, amount)),
                Snap(Vector2.Lerp(from.Size, to.Size, amount)),
                from.Scale + (to.Scale - from.Scale) * amount,
                LerpColor(from.Modulate, to.Modulate, amount));
        }

        private static Vector2 Snap(Vector2 value)
        {
            return new Vector2(MathF.Round(value.X), MathF.Round(value.Y));
        }

        private static Color LerpColor(Color from, Color to, float amount)
        {
            return new Color(
                from.R + (to.R - from.R) * amount,
                from.G + (to.G - from.G) * amount,
                from.B + (to.B - from.B) * amount,
                from.A + (to.A - from.A) * amount);
        }
    }

    private sealed class PreviewNode : LayoutContainer
    {
        private readonly ProfilePreviewSpriteView _preview;
        private HumanoidCharacterProfile? _loadedProfile;
        private bool _interactive;
        private int _navigateDirection;

        public event Action<int>? Pressed;

        public int? Slot { get; private set; }
        public bool HasCharacter => Slot != null;

        public PreviewNode()
        {
            RectClipContent = false;
            MouseFilter = MouseFilterMode.Ignore;
            SetSize = PreviewBounds;

            _preview = new ProfilePreviewSpriteView
            {
                OverrideDirection = Direction.South,
                Stretch = SpriteView.StretchMode.None,
                MouseFilter = MouseFilterMode.Ignore,
                Modulate = Color.White,
                SetSize = PreviewBounds,
            };
            AddChild(_preview);

            OnKeyBindDown += HandlePressed;
        }

        public void SetCharacter(HumanoidCharacterProfile? profile, int? slot)
        {
            if (slot == null || profile == null)
            {
                Slot = null;
                _loadedProfile = null;
                _preview.ClearPreview();
                _preview.Modulate = Color.White;
                Visible = false;
                return;
            }

            var needsReload = Slot != slot || !ReferenceEquals(_loadedProfile, profile) || !_preview.HasValidPreviewDummy;
            if (needsReload)
                _preview.LoadPreview(profile);

            Slot = slot;
            _loadedProfile = profile;
            Visible = true;
        }

        public void ApplyPose(PoseState state, bool allowInteraction)
        {
            ApplyPose(state, allowInteraction, 0);
        }

        public void ApplyPose(PoseState state, bool allowInteraction, int navigateDirection)
        {
            SetSize = state.Size;
            SetPosition(this, new Vector2(MathF.Round(state.Position.X), MathF.Round(state.Position.Y)));
            Modulate = Color.White;
            _preview.Modulate = state.Modulate;
            _preview.Scale = new Vector2(state.Scale, state.Scale);
            _preview.SetSize = state.Size;
            SetPosition(_preview, Vector2.Zero);
            _navigateDirection = allowInteraction ? Math.Sign(navigateDirection) : 0;
            SetInteractive(allowInteraction && HasCharacter);

            if (!HasCharacter)
                Visible = false;
        }

        protected override void Resized()
        {
            base.Resized();
            _preview.SetSize = Size;
            SetPosition(_preview, Vector2.Zero);
        }

        private void SetInteractive(bool interactive)
        {
            _interactive = interactive;
            MouseFilter = interactive ? MouseFilterMode.Stop : MouseFilterMode.Ignore;
            DefaultCursorShape = interactive ? CursorShape.Hand : CursorShape.Arrow;
        }

        private void HandlePressed(GUIBoundKeyEventArgs args)
        {
            if (!_interactive || _navigateDirection == 0 || args.Handled || args.Function != EngineKeyFunctions.UIClick)
                return;

            args.Handle();
            Pressed?.Invoke(_navigateDirection);
        }
    }

    private sealed class CarouselArrowControl : Control
    {
        private static readonly Color ChevronColor = new(0.86f, 0.73f, 0.39f, 0.94f);
        private const float ChevronThickness = 2.25f;
        private readonly bool _pointsLeft;
        private bool _available;
        private bool _busy;

        public event Action? Pressed;

        public CarouselArrowControl(bool pointsLeft)
        {
            _pointsLeft = pointsLeft;
            MouseFilter = MouseFilterMode.Ignore;
            DefaultCursorShape = CursorShape.Hand;
            OnKeyBindDown += HandlePressed;
        }

        public void SetState(bool available, bool busy)
        {
            _available = available;
            _busy = busy;
            Visible = available;
            MouseFilter = available && !busy ? MouseFilterMode.Stop : MouseFilterMode.Ignore;
            DefaultCursorShape = available && !busy ? CursorShape.Hand : CursorShape.Arrow;
            InvalidateArrange();
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            if (!_available || _busy)
                return;

            var width = PixelWidth;
            var height = PixelHeight;
            if (width <= 0 || height <= 0)
                return;

            var startX = _pointsLeft ? width * 0.8f : width * 0.2f;
            var endX = _pointsLeft ? width * 0.2f : width * 0.8f;
            var midY = height * 0.5f;
            var topY = height * 0.22f;
            var bottomY = height * 0.78f;

            var top = new Vector2(startX, topY);
            var bottom = new Vector2(startX, bottomY);
            var tip = new Vector2(endX, midY);

            var vertices = new Vector2[12];
            BuildThickSegment(top, tip, ChevronThickness * 0.5f, vertices, 0);
            BuildThickSegment(bottom, tip, ChevronThickness * 0.5f, vertices, 6);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, vertices, ChevronColor);
        }

        private static void BuildThickSegment(Vector2 start, Vector2 end, float halfWidth, Vector2[] vertices, int offset)
        {
            var direction = end - start;
            var normal = direction.LengthSquared() <= 0.001f
                ? Vector2.Zero
                : Vector2.Normalize(new Vector2(-direction.Y, direction.X)) * halfWidth;

            vertices[offset + 0] = start + normal;
            vertices[offset + 1] = end + normal;
            vertices[offset + 2] = end - normal;
            vertices[offset + 3] = start + normal;
            vertices[offset + 4] = end - normal;
            vertices[offset + 5] = start - normal;
        }

        private void HandlePressed(GUIBoundKeyEventArgs args)
        {
            if (!_available || _busy || args.Handled || args.Function != EngineKeyFunctions.UIClick)
                return;

            args.Handle();
            Pressed?.Invoke();
        }
    }
}
