using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._RMC14.Vendors;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Mind;
using Content.Shared.Power.Components;
using Content.Shared.Projectiles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static System.StringComparison;
using static Robust.Client.UserInterface.Controls.LineEdit;

namespace Content.Client._RMC14.Vendors;

[UsedImplicitly]
public sealed class CMAutomatedVendorBui : BoundUserInterface
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ILocalizationManager _loc = default!;

    private readonly SharedJobSystem _job;
    private readonly SharedMindSystem _mind;
    private readonly SpriteSystem _spriteSystem;

    private CMAutomatedVendorWindow? _window;
    private CMAutomatedVendorEntry? _selectedEntry;

    private sealed class EntryInfo
    {
        public string Name = "";
        public string Description = "";
        public List<Texture> Textures = new();
        public Color Modulate = Color.White;
        // Ranged weapon stats
        public bool IsWeapon;
        public string? Damage;
        public string? FireRate;
        public string? FireModes;
        public string? AmmoCapacity;
        public string? Dps;
        // Melee weapon stats
        public bool IsMelee;
        public string? MeleeDamage;
        public string? MeleeSpeed;
        public string? MeleeRange;
        public string? MeleeDps;
        // Ammo/magazine stats
        public bool IsAmmo;
        public string? AmmoCapStr;
        public string? AmmoDamageStr;
    }

    private readonly Dictionary<CMAutomatedVendorEntry, EntryInfo> _entryInfos = new();

    public CMAutomatedVendorBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _job = EntMan.System<SharedJobSystem>();
        _mind = EntMan.System<SharedMindSystem>();
        _spriteSystem = EntMan.System<SpriteSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CMAutomatedVendorWindow>();
        _window.Title = EntMan.GetComponentOrNull<MetaDataComponent>(Owner)?.EntityName
            ?? Loc.GetString("cm-automated-vendor-ui-title-fallback");

        BuildEntries();
        _window.Search.OnTextChanged += OnSearchChanged;
        Refresh();
    }

    private void BuildEntries()
    {
        if (_window == null)
            return;

        _window.Sections.DisposeAllChildren();
        _entryInfos.Clear();
        _selectedEntry = null;
        _window.DetailPanel.Visible = false;
        var user = EntMan.GetComponentOrNull<CMVendorUserComponent>(_player.LocalEntity);

        if (!EntMan.TryGetComponent(Owner, out CMAutomatedVendorComponent? vendor))
            return;

        for (var sectionIndex = 0; sectionIndex < vendor.Sections.Count; sectionIndex++)
        {
            var section = vendor.Sections[sectionIndex];
            var uiSection = new CMAutomatedVendorSection { Section = section };
            uiSection.Label.SetMessage(GetSectionName(user, section, _loc));

            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                var entry = section.Entries[entryIndex];
                var uiEntry = new CMAutomatedVendorEntry();
                uiEntry.Panel.Button.TextLabel.Text = entry.Name ?? entry.Id;

                var info = new EntryInfo();

                if (_prototype.TryIndex(entry.Id, out var entity))
                {
                    uiEntry.Texture.Textures = _spriteSystem.GetPrototypeTextures(entity)
                        .Select(layer => layer.Default)
                        .ToList();
                    if (entity.TryGetComponent<SpriteComponent>("Sprite", out var sprites))
                        uiEntry.Texture.Modulate = sprites.AllLayers.First().Color;

                    if (entry.Name == null)
                        uiEntry.Panel.Button.TextLabel.Text = entity.Name;

                    info.Name = entity.Name;
                    info.Description = entity.Description ?? "";
                    info.Textures = _spriteSystem.GetPrototypeTextures(entity)
                        .Select(layer => layer.Default)
                        .ToList();
                    if (entity.TryGetComponent<SpriteComponent>("Sprite", out var sprCopy))
                        info.Modulate = sprCopy.AllLayers.First().Color;

                    // Extract weapon stats from prototype
                    if (entity.TryGetComponent<GunComponent>("Gun", out var gun))
                    {
                        info.IsWeapon = true;
                        info.FireRate = $"{gun.FireRate:F1} /s";

                        var modes = new List<string>();
                        if ((gun.AvailableModes & SelectiveFire.SemiAuto) != 0)
                            modes.Add(Loc.GetString("cm-automated-vendor-ui-detail-mode-semi"));
                        if ((gun.AvailableModes & SelectiveFire.Burst) != 0)
                            modes.Add(Loc.GetString("cm-automated-vendor-ui-detail-mode-burst", ("count", gun.ShotsPerBurst)));
                        if ((gun.AvailableModes & SelectiveFire.FullAuto) != 0)
                            modes.Add(Loc.GetString("cm-automated-vendor-ui-detail-mode-auto"));
                        info.FireModes = modes.Count > 0 ? string.Join(", ", modes) : "—";

                        // 1) Direct ballistic ammo on the gun itself
                        if (entity.TryGetComponent<BallisticAmmoProviderComponent>("BallisticAmmoProvider", out var ballistic))
                        {
                            var capacity = ballistic.Capacity;
                            info.AmmoCapacity = capacity.ToString();
                            TryExtractProjectileDamage(ballistic, out var dmg);
                            info.Damage = dmg;
                        }
                        // 2) Magazine-based gun: look up starting magazine from ItemSlots
                        else if (entity.TryGetComponent<ItemSlotsComponent>("ItemSlots", out var itemSlots))
                        {
                            var slots = itemSlots.Slots;
                            if (slots.TryGetValue("gun_magazine", out var magSlot) &&
                                magSlot.StartingItem is { } magProtoId &&
                                _prototype.TryIndex(magProtoId, out var magEntity))
                            {
                                // 2a) Ballistic magazine
                                if (magEntity.TryGetComponent<BallisticAmmoProviderComponent>("BallisticAmmoProvider", out var magBallistic))
                                {
                                    var magCapacity = magBallistic.Capacity;
                                    info.AmmoCapacity = magCapacity.ToString();
                                    TryExtractProjectileDamage(magBallistic, out var dmg);
                                    info.Damage = dmg;
                                }
                                // 2b) Battery power cell magazine
                                else if (magEntity.TryGetComponent<BatteryAmmoProviderComponent>("BatteryAmmoProvider", out var magBattery))
                                {
                                    if (magEntity.TryGetComponent<BatteryComponent>("Battery", out var magBatteryComp))
                                    {
                                        var magMaxCharge = magBatteryComp.MaxCharge;
                                        var magFireCost = magBattery.FireCost;
                                        if (magFireCost > 0)
                                            info.AmmoCapacity = ((int)(magMaxCharge / magFireCost)).ToString();
                                    }

                                    var magBatteryProto = magBattery.Prototype;
                                    if (_prototype.TryIndex(magBatteryProto, out var magHitscanEntity))
                                    {
                                        if (magHitscanEntity.TryGetComponent<HitscanBasicDamageComponent>("HitscanBasicDamage", out var magHitscanDmg))
                                            info.Damage = magHitscanDmg.Damage.GetTotal().ToString();
                                        else if (magHitscanEntity.TryGetComponent<ProjectileComponent>("Projectile", out var magProj))
                                            info.Damage = magProj.Damage.GetTotal().ToString();
                                    }
                                }
                            }
                        }
                        // 3) Battery/laser gun
                        else if (entity.TryGetComponent<BatteryAmmoProviderComponent>("BatteryAmmoProvider", out var battery))
                        {
                            if (entity.TryGetComponent<BatteryComponent>("Battery", out var batteryComp))
                            {
                                var maxCharge = batteryComp.MaxCharge;
                                var fireCost = battery.FireCost;
                                if (fireCost > 0)
                                    info.AmmoCapacity = ((int)(maxCharge / fireCost)).ToString();
                            }

                            // Get damage from the hitscan/projectile prototype
                            var batteryProto = battery.Prototype;
                            if (_prototype.TryIndex(batteryProto, out var hitscanEntity))
                            {
                                if (hitscanEntity.TryGetComponent<HitscanBasicDamageComponent>("HitscanBasicDamage", out var hitscanDmg))
                                    info.Damage = hitscanDmg.Damage.GetTotal().ToString();
                                else if (hitscanEntity.TryGetComponent<ProjectileComponent>("Projectile", out var proj))
                                    info.Damage = proj.Damage.GetTotal().ToString();
                            }
                        }

                        info.Damage ??= "—";
                        info.AmmoCapacity ??= "—";

                        // Calculate DPS = damage × fireRate × 5
                        if (float.TryParse(info.Damage, out var dmgVal) && dmgVal > 0)
                        {
                            var dps5 = dmgVal * gun.FireRate * 5f;
                            info.Dps = $"{dps5:F0}";
                        }
                        else
                        {
                            info.Dps = "—";
                        }
                    }

                    // Melee weapon stats
                    if (!info.IsWeapon && entity.TryGetComponent<MeleeWeaponComponent>("MeleeWeapon", out var melee))
                    {
                        info.IsMelee = true;
                        var totalDmg = melee.Damage.GetTotal();
                        info.MeleeDamage = totalDmg.ToString();
                        info.MeleeSpeed = $"{melee.AttackRate:F1} /s";
                        info.MeleeRange = $"{melee.Range:F1}";
                        var meleeDps5 = (float) totalDmg * melee.AttackRate * 5f;
                        info.MeleeDps = $"{meleeDps5:F0}";
                    }

                    // Ammo/magazine stats
                    if (!info.IsWeapon && !info.IsMelee)
                    {
                        if (entity.TryGetComponent<BallisticAmmoProviderComponent>("BallisticAmmoProvider", out var ammoBallistic))
                        {
                            info.IsAmmo = true;
                            var ammoCap = ammoBallistic.Capacity;
                            info.AmmoCapStr = ammoCap.ToString();
                            TryExtractProjectileDamage(ammoBallistic, out var ammoDmg);
                            info.AmmoDamageStr = ammoDmg ?? "—";
                        }
                        else if (entity.TryGetComponent<BatteryAmmoProviderComponent>("BatteryAmmoProvider", out var ammoBattery))
                        {
                            info.IsAmmo = true;
                            if (entity.TryGetComponent<BatteryComponent>("Battery", out var ammoBattComp))
                            {
                                var amMaxCharge = ammoBattComp.MaxCharge;
                                var amFireCost = ammoBattery.FireCost;
                                if (amFireCost > 0)
                                    info.AmmoCapStr = ((int)(amMaxCharge / amFireCost)).ToString();
                            }
                            var amBattProto = ammoBattery.Prototype;
                            if (_prototype.TryIndex(amBattProto, out var amHitscanEntity))
                            {
                                if (amHitscanEntity.TryGetComponent<HitscanBasicDamageComponent>("HitscanBasicDamage", out var amHitscanDmg))
                                    info.AmmoDamageStr = amHitscanDmg.Damage.GetTotal().ToString();
                                else if (amHitscanEntity.TryGetComponent<ProjectileComponent>("Projectile", out var amProj))
                                    info.AmmoDamageStr = amProj.Damage.GetTotal().ToString();
                            }
                            info.AmmoCapStr ??= "—";
                            info.AmmoDamageStr ??= "—";
                        }
                    }
                }
                else
                {
                    info.Name = entry.Name ?? entry.Id;
                }

                _entryInfos[uiEntry] = info;

                var sectionI = sectionIndex;
                var entryI = entryIndex;

                uiEntry.Panel.Button.OnPressed += _ => SendMessage(new CMVendorVendBuiMsg(sectionI, entryI));
                uiEntry.InfoButton.OnPressed += _ => OnInfoPressed(uiEntry);

                if (entry.Recommended)
                {
                    uiEntry.Panel.Button.TextLabel.Text = $"✠ {uiEntry.Panel.Button.TextLabel.Text}";
                    uiEntry.Panel.Color = Color.FromHex("#0C160C");
                    uiEntry.Panel.BorderColor = Color.FromHex("#3A6A28");
                    uiEntry.Panel.HoveredColor = Color.FromHex("#1E3A14");
                }

                // Truncate long display names but keep full name for search and tooltip
                var fullText = uiEntry.Panel.Button.TextLabel.Text ?? "";
                if (fullText.Length > 24)
                {
                    uiEntry.Panel.Button.ToolTip = fullText;
                    uiEntry.Panel.Button.TextLabel.Text = fullText[..22] + "...";
                }

                if (section.TakeAll != null || section.TakeOne != null)
                {
                    uiEntry.Panel.Color = Color.FromHex("#141008");
                    uiEntry.Panel.BorderColor = Color.FromHex("#5A4A1E");
                    uiEntry.Panel.HoveredColor = Color.FromHex("#2A2210");
                }

                uiEntry.AnimateAppear(entryIndex * 0.03f);
                uiSection.Entries.AddChild(uiEntry);
            }

            _window.Sections.AddChild(uiSection);
        }
    }

    private void OnInfoPressed(CMAutomatedVendorEntry entry)
    {
        if (_window == null)
            return;

        // Deselect previous entry's info highlight
        _selectedEntry?.SetInfoActive(false);

        if (_selectedEntry == entry)
        {
            _window.DetailPanel.Visible = false;
            _selectedEntry = null;
            return;
        }

        _selectedEntry = entry;
        entry.SetInfoActive(true);

        if (!_entryInfos.TryGetValue(entry, out var info))
            return;

        _window.DetailPanel.DetailTitle.Text = info.Name;
        _window.DetailPanel.DetailTexture.Textures = info.Textures;
        _window.DetailPanel.DetailTexture.Modulate = info.Modulate;
        _window.DetailPanel.DetailItemName.Text = info.Name;

        var desc = new FormattedMessage();
        desc.PushColor(Color.FromHex("#A09880"));
        desc.AddText(string.IsNullOrWhiteSpace(info.Description)
            ? Loc.GetString("cm-automated-vendor-ui-detail-desc")
            : info.Description);
        desc.Pop();
        _window.DetailPanel.DetailDescription.SetMessage(desc);

        // Weapon stats
        _window.DetailPanel.WeaponStatsSection.Visible = info.IsWeapon;
        if (info.IsWeapon)
        {
            _window.DetailPanel.DetailDamage.Text = info.Damage ?? "—";
            _window.DetailPanel.DetailDps.Text = info.Dps ?? "—";
            _window.DetailPanel.DetailFireRate.Text = info.FireRate ?? "—";
            _window.DetailPanel.DetailFireMode.Text = info.FireModes ?? "—";
            _window.DetailPanel.DetailAmmo.Text = info.AmmoCapacity ?? "—";
        }

        // Melee stats
        _window.DetailPanel.MeleeStatsSection.Visible = info.IsMelee;
        if (info.IsMelee)
        {
            _window.DetailPanel.DetailMeleeDamage.Text = info.MeleeDamage ?? "—";
            _window.DetailPanel.DetailMeleeDps.Text = info.MeleeDps ?? "—";
            _window.DetailPanel.DetailMeleeSpeed.Text = info.MeleeSpeed ?? "—";
            _window.DetailPanel.DetailMeleeRange.Text = info.MeleeRange ?? "—";
        }

        // Ammo stats
        _window.DetailPanel.AmmoStatsSection.Visible = info.IsAmmo;
        if (info.IsAmmo)
        {
            _window.DetailPanel.DetailAmmoCap.Text = info.AmmoCapStr ?? "—";
            _window.DetailPanel.DetailAmmoDamage.Text = info.AmmoDamageStr ?? "—";
        }

        _window.DetailPanel.Visible = true;
        _window.DetailPanel.AnimateOpen();
    }

    private void TryExtractProjectileDamage(BallisticAmmoProviderComponent ballistic, out string? damage)
    {
        damage = null;
        var ammoProto = ballistic.Proto;
        if (ammoProto is not { } ammoProtoId || !_prototype.TryIndex(ammoProtoId, out var ammoEntity))
            return;

        if (ammoEntity.TryGetComponent<CartridgeAmmoComponent>("CartridgeAmmo", out var cartridge) &&
            _prototype.TryIndex(cartridge.Prototype, out var projectileEntity))
        {
            if (projectileEntity.TryGetComponent<ProjectileComponent>("Projectile", out var proj))
                damage = proj.Damage.GetTotal().ToString();
        }
        else if (ammoEntity.TryGetComponent<ProjectileComponent>("Projectile", out var directProj))
        {
            damage = directProj.Damage.GetTotal().ToString();
        }
    }

    private bool IsSectionValid(CMVendorSection section)
    {
        if (section.Jobs.Count == 0)
            return true;

        if (_player.LocalSession == null || !_mind.TryGetMind(_player.LocalSession.UserId, out var mindId))
            return false;

        foreach (var job in section.Jobs)
        {
            if (_job.MindHasJobWithId(mindId, job.Id))
                return true;
        }

        return false;
    }

    private void OnSearchChanged(LineEditEventArgs args)
    {
        if (_window == null)
            return;

        foreach (var sectionControl in _window.Sections.Children)
        {
            if (sectionControl is not CMAutomatedVendorSection section)
                continue;

            var anyVisible = false;
            foreach (var entryControl in section.Entries.Children)
            {
                if (entryControl is not CMAutomatedVendorEntry entry)
                    continue;

                entry.Visible = string.IsNullOrWhiteSpace(args.Text) ||
                                (entry.Panel.Button.TextLabel.Text?.Contains(args.Text, OrdinalIgnoreCase) ?? false) ||
                                (entry.Panel.Button.ToolTip?.Contains(args.Text, OrdinalIgnoreCase) ?? false);
                anyVisible |= entry.Visible;
            }

            section.Visible = anyVisible && (section.Section == null || IsSectionValid(section.Section));
        }
    }

    public void Refresh()
    {
        if (_window == null || !EntMan.TryGetComponent(Owner, out CMAutomatedVendorComponent? vendor))
            return;

        var user = EntMan.GetComponentOrNull<CMVendorUserComponent>(_player.LocalEntity);
        var userPoints = user?.Points ?? 0;
        var anyEntryWithPoints = false;

        for (var sectionIndex = 0; sectionIndex < vendor.Sections.Count; sectionIndex++)
        {
            var section = vendor.Sections[sectionIndex];
            var uiSection = (CMAutomatedVendorSection) _window.Sections.GetChild(sectionIndex);
            uiSection.Label.SetMessage(GetSectionName(user, section, _loc));

            var sectionDisabled = !IsSectionValid(section);
            if (section.Choices is { } choices)
            {
                var picked = user?.Choices.GetValueOrDefault(choices.Id) ?? 0;
                if (picked >= choices.Amount)
                    sectionDisabled = true;
            }

            var anyAmount = false;
            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                var entry = section.Entries[entryIndex];
                var uiEntry = (CMAutomatedVendorEntry) uiSection.Entries.GetChild(entryIndex);

                var disabled = sectionDisabled || entry.Amount is <= 0;
                if (section.TakeAll is { Length: > 0 } takeAllId)
                {
                    var key = $"{takeAllId}:{entry.Id}";
                    if (user?.TakeAll.GetValueOrDefault(key) == true)
                        disabled = true;
                }

                if (section.TakeOne is { Length: > 0 } takeOneId && user?.TakeOne.GetValueOrDefault(takeOneId) == true)
                    disabled = true;

                if (entry.Points is { } points)
                {
                    anyEntryWithPoints = true;
                    uiEntry.Amount.Text = $"{points}P";
                    if (user == null || userPoints < points)
                        disabled = true;
                }
                else if (entry.Amount is { } amount)
                {
                    uiEntry.Amount.Text = amount.ToString();
                }
                else
                {
                    uiEntry.Amount.Text = Loc.GetString("cm-automated-vendor-ui-unlimited");
                }

                uiEntry.Amount.Modulate = disabled ? Color.FromHex("#8B3030") : Color.White;
                uiEntry.Panel.Button.Disabled = disabled;

                if (!string.IsNullOrWhiteSpace(uiEntry.Amount.Text))
                    anyAmount = true;
            }

            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                var uiEntry = (CMAutomatedVendorEntry) uiSection.Entries.GetChild(entryIndex);
                uiEntry.Amount.Visible = anyAmount;
            }
        }

        _window.PointsLabel.Text = anyEntryWithPoints
            ? Loc.GetString("cm-automated-vendor-ui-points", ("points", userPoints))
            : string.Empty;
        _window.PointsLabel.Visible = anyEntryWithPoints;
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is CMVendorRefreshBuiMsg)
            Refresh();
    }

    private static FormattedMessage GetSectionName(CMVendorUserComponent? user, CMVendorSection section, ILocalizationManager loc)
    {
        var name = new FormattedMessage();
        var sectionName = loc.TryGetString(section.Name, out var localizedName)
            ? localizedName
            : section.Name;
        name.PushColor(Color.FromHex("#BFA555"));
        name.PushTag(new MarkupNode("bold", null, null));
        name.AddText(sectionName.ToUpperInvariant());

        if (section.TakeAll is { Length: > 0 })
        {
            var pending = false;
            foreach (var entry in section.Entries)
            {
                var key = $"{section.TakeAll}:{entry.Id}";
                if (user?.TakeAll.GetValueOrDefault(key) != true)
                {
                    pending = true;
                    break;
                }
            }

            if (pending)
                name.AddText($" ({Loc.GetString("cm-automated-vendor-ui-take-all")})");
        }
        else if (section.TakeOne is { Length: > 0 })
        {
            if (user?.TakeOne.GetValueOrDefault(section.TakeOne) != true)
                name.AddText($" ({Loc.GetString("cm-automated-vendor-ui-take-one")})");
        }
        else if (section.Choices is { } choices)
        {
            var selected = user?.Choices.GetValueOrDefault(choices.Id) ?? 0;
            var left = Math.Max(0, choices.Amount - selected);
            if (left > 0)
                name.AddText(
                    $" ({Loc.GetString("cm-automated-vendor-ui-choose-left", ("count", left))})");
        }

        name.Pop();
        name.Pop();
        return name;
    }
}

