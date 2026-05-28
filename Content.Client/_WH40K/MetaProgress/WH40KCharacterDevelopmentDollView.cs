using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Lobby.UI.ProfileEditorControls;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared._WH40K.MetaProgress;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Utility;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.MetaProgress;

public sealed partial class WH40KCharacterDevelopmentDollView : LayoutContainer
{
	private sealed record OrganTextureInfo(TextureRect Rect, float VisibleWidthFactor, Vector2 BodyAnchor, Vector2 VisibleCenterFraction, Vector2 VisibleSizeFraction);

	[Dependency]
	private  IResourceCache _resources = default!;

	private static readonly Vector2 PreviewFillRatio = new Vector2(0.92f, 0.96f);

	private static readonly UIBox2 PreviewBodyRegion = new UIBox2(0.32f, 0.1f, 0.68f, 0.9f);

	private static readonly ResPath HumanOrgansRsiPath = new ResPath("/Textures/Mobs/Species/Human/organs.rsi");

	private readonly ProfilePreviewSpriteView _preview;

	private readonly Dictionary<WH40KCharacterDevelopmentOrganType, OrganTextureInfo[]> _organTextures = new Dictionary<WH40KCharacterDevelopmentOrganType, OrganTextureInfo[]>();

	private WH40KCharacterDevelopmentOrganType? _activeOrgan;

	private Color _activeAccent = Color.White;

	public WH40KCharacterDevelopmentDollView()
	{
		IoCManager.InjectDependencies(this);
		base.MouseFilter = MouseFilterMode.Ignore;
		base.RectClipContent = false;
		_preview = new ProfilePreviewSpriteView
		{
			Scale = Vector2.One,
			Stretch = SpriteView.StretchMode.Fill,
			OverrideDirection = Direction.South,
			MouseFilter = MouseFilterMode.Ignore
		};
		AddChild(_preview);
		BuildOrganVisuals();
		SetActiveOrgan(null, Color.White);
	}

	public void SetProfile(HumanoidCharacterProfile? profile, JobPrototype? jobOverride, bool showClothes)
	{
		if (profile == null)
		{
			ClearPreview();
		}
		else
		{
			_preview.LoadPreview(profile, jobOverride, showClothes);
		}
	}

	public void ReloadProfile(HumanoidCharacterProfile? profile)
	{
		if (profile == null)
		{
			ClearPreview();
		}
		else
		{
			_preview.ReloadProfilePreview(profile);
		}
	}

	public void ClearPreview()
	{
		_preview.ClearPreview();
	}

	public Vector2 GetOrganAnchorFraction(WH40KCharacterDevelopmentOrganType organ)
	{
		if (!_organTextures.TryGetValue(organ, out var value) || value is not { Length: > 0 })
		{
			return new Vector2(0.5f, 0.5f);
		}
		Vector2 zero = Vector2.Zero;
		OrganTextureInfo[] array = value;
		foreach (OrganTextureInfo organTextureInfo in array)
		{
			zero += ResolveFullAnchorFraction(organTextureInfo.BodyAnchor);
		}
		return zero / value.Length;
	}

	public void SetActiveOrgan(WH40KCharacterDevelopmentOrganType? organ, Color accent)
	{
		_activeOrgan = organ;
		_activeAccent = accent;
		float newA = ((!organ.HasValue) ? 1f : 0.34f);
		_preview.Modulate = Color.White.WithAlpha(newA);

		foreach (var (key, value) in _organTextures)
		{
			var flag = organ == key;
			foreach (var organTextureInfo in value)
			{
				organTextureInfo.Rect.Visible = flag;
				organTextureInfo.Rect.Modulate = flag
					? Blend(accent, Color.White, 0.48f).WithAlpha(0.96f)
					: Color.White.WithAlpha(0f);
			}
		}
	}

	protected override Vector2 ArrangeOverride(Vector2 finalSize)
	{
		Vector2 vector = new Vector2(finalSize.X * PreviewFillRatio.X, finalSize.Y * PreviewFillRatio.Y);
		Vector2 vector2 = (finalSize - vector) * 0.5f;
		_preview.SetSize = vector;
		LayoutContainer.SetPosition(_preview, vector2);
		Vector2 vector3 = vector2 + vector * PreviewBodyRegion.TopLeft;
		Vector2 vector4 = vector * PreviewBodyRegion.Size;
		foreach (OrganTextureInfo[] value in _organTextures.Values)
		{
			foreach (OrganTextureInfo organTextureInfo in value)
			{
				float num = MathF.Max(0.01f, organTextureInfo.VisibleSizeFraction.X);
				float num2 = vector4.X * organTextureInfo.VisibleWidthFactor / num;
				var texture = organTextureInfo.Rect.Texture;
				float num3 = ((texture == null || texture.Width <= 0) ? 1f : ((float)texture.Height / (float)texture.Width));
				Vector2 setSize = new Vector2(num2, num2 * num3);
				organTextureInfo.Rect.SetSize = setSize;
				Vector2 vector5 = vector3 + vector4 * organTextureInfo.BodyAnchor;
				LayoutContainer.SetPosition(position: vector5 - new Vector2(setSize.X * organTextureInfo.VisibleCenterFraction.X, setSize.Y * organTextureInfo.VisibleCenterFraction.Y), control: organTextureInfo.Rect);
			}
		}
		return base.ArrangeOverride(finalSize);
	}

	protected override void Draw(DrawingHandleScreen handle)
	{
		base.Draw(handle);
		if (_activeOrgan.HasValue)
		{
			Vector2 position = new Vector2((float)base.PixelWidth * 0.5f, (float)base.PixelHeight * 0.5f);
			float num = MathF.Min(base.PixelWidth, base.PixelHeight) * 0.22f;
			handle.DrawCircle(position, num, _activeAccent.WithAlpha(0.05f));
			handle.DrawCircle(position, num * 0.76f, _activeAccent.WithAlpha(0.08f));
		}
	}

	private void BuildOrganVisuals()
	{
		_organTextures[WH40KCharacterDevelopmentOrganType.Brain] = new OrganTextureInfo[1] { CreateOrganRect(WH40KCharacterDevelopmentOrganType.Brain, "brain", 0.4f, new Vector2(0.46f, 0.21f), new Vector2(0.469f, 0.469f), new Vector2(0.406f, 0.344f)) };
		_organTextures[WH40KCharacterDevelopmentOrganType.Heart] = new OrganTextureInfo[1] { CreateOrganRect(WH40KCharacterDevelopmentOrganType.Heart, "heart-on", 0.52f, new Vector2(0.48f, 0.41f), new Vector2(0.469f, 0.453f), new Vector2(0.609f, 0.641f)) };
		_organTextures[WH40KCharacterDevelopmentOrganType.Lungs] = new OrganTextureInfo[2]
		{
			CreateOrganRect(WH40KCharacterDevelopmentOrganType.Lungs, "lung-l", 0.28f, new Vector2(0.34f, 0.4f), new Vector2(0.375f, 0.484f), new Vector2(0.219f, 0.438f)),
			CreateOrganRect(WH40KCharacterDevelopmentOrganType.Lungs, "lung-r", 0.28f, new Vector2(0.56f, 0.4f), new Vector2(0.562f, 0.484f), new Vector2(0.219f, 0.438f))
		};
		_organTextures[WH40KCharacterDevelopmentOrganType.Kidneys] = new OrganTextureInfo[2]
		{
			CreateOrganRect(WH40KCharacterDevelopmentOrganType.Kidneys, "kidney-l", 0.14f, new Vector2(0.22f, 0.53f), new Vector2(0.359f, 0.5f), new Vector2(0.25f, 0.406f)),
			CreateOrganRect(WH40KCharacterDevelopmentOrganType.Kidneys, "kidney-r", 0.14f, new Vector2(0.78f, 0.53f), new Vector2(0.578f, 0.5f), new Vector2(0.25f, 0.406f))
		};
		_organTextures[WH40KCharacterDevelopmentOrganType.Liver] = new OrganTextureInfo[1] { CreateOrganRect(WH40KCharacterDevelopmentOrganType.Liver, "liver", 0.4f, new Vector2(0.34f, 0.48f), new Vector2(0.484f, 0.531f), new Vector2(0.438f, 0.344f)) };
		_organTextures[WH40KCharacterDevelopmentOrganType.Stomach] = new OrganTextureInfo[1] { CreateOrganRect(WH40KCharacterDevelopmentOrganType.Stomach, "stomach", 0.38f, new Vector2(0.58f, 0.47f), new Vector2(0.469f, 0.453f), new Vector2(0.469f, 0.562f)) };
	}

	private OrganTextureInfo CreateOrganRect(WH40KCharacterDevelopmentOrganType organ, string stateId, float visibleWidthFactor, Vector2 bodyAnchor, Vector2 visibleCenterFraction, Vector2 visibleSizeFraction)
	{
		TextureRect textureRect = new TextureRect
		{
			Stretch = TextureRect.StretchMode.KeepAspectCentered,
			CanShrink = true,
			Visible = false,
			Modulate = Color.White.WithAlpha(0f),
			MouseFilter = MouseFilterMode.Ignore
		};
		textureRect.Texture = ResolveTexture(stateId);
		AddChild(textureRect);
		return new OrganTextureInfo(textureRect, visibleWidthFactor, bodyAnchor, visibleCenterFraction, visibleSizeFraction);
	}

	private Texture? ResolveTexture(string stateId)
	{
		if (!_resources.TryGetResource<RSIResource>(HumanOrgansRsiPath, out RSIResource? resource) ||
			resource == null)
		{
			return null;
		}

		if (!resource.RSI.TryGetState(stateId, out RSI.State? state) ||
			state == null ||
			state.DelayCount <= 0)
		{
			return null;
		}
		return state.GetFrame(Direction.South.Convert(state.RsiDirections), 0);
	}

	private static Vector2 ResolveFullAnchorFraction(Vector2 bodyAnchor)
	{
		Vector2 vector = (Vector2.One - PreviewFillRatio) * 0.5f;
		Vector2 vector2 = PreviewBodyRegion.TopLeft + PreviewBodyRegion.Size * bodyAnchor;
		return vector + vector2 * PreviewFillRatio;
	}

	private static Color Blend(Color from, Color to, float amount)
	{
		float blend = MathHelper.Clamp01(amount);
		return new Color(MathHelper.Lerp(from.R, to.R, blend), MathHelper.Lerp(from.G, to.G, blend), MathHelper.Lerp(from.B, to.B, blend), MathHelper.Lerp(from.A, to.A, blend));
	}
}
