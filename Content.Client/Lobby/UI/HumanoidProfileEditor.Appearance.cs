using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WH40K.MetaProgress;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared.Guidebook;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Speech;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    public event Action<List<ProtoId<GuideEntryPrototype>>>? OnOpenGuidebook;

    private ColorSelectorSliders _rgbSkinColorSelector;
    private bool _updatingSkinControls;
    private List<SpeciesPrototype> _species = new();
    private static readonly ProtoId<GuideEntryPrototype> DefaultSpeciesGuidebook = "Species";

    public void UpdateSpeciesGuidebookIcon()
    {
        SpeciesInfoButton.StyleClasses.Clear();

        var species = Profile?.Species;
        if (species is null)
            return;

        if (!_prototypeManager.Resolve<SpeciesPrototype>(species, out var speciesProto))
            return;

        // Don't display the info button if no guide entry is found
        if (!_prototypeManager.HasIndex<GuideEntryPrototype>(species))
            return;

        const string style = "SpeciesInfoDefault";
        SpeciesInfoButton.StyleIdentifier = style;
    }

    private void UpdateGenderControls()
    {
        if (Profile == null)
        {
            return;
        }

        PronounsButton.SelectId((int)Profile.Gender);
    }

    private void UpdateAgeEdit()
    {
        AgeEdit.Text = Profile?.Age.ToString() ?? "";
    }

    private void UpdateVoiceToneControls()
    {
        if (Profile == null)
            return;

        VoiceToneButton.SelectId((int) Profile.VoiceTone);
    }

    private void UpdateSexControls()
    {
        if (Profile == null)
            return;

        SexButton.Clear();

        var sexes = new List<Sex>();

        // add species sex options, default to just none if we are in bizzaro world and have no species
        if (_prototypeManager.Resolve(Profile.Species, out var speciesProto))
        {
            foreach (var sex in speciesProto.Sexes)
            {
                sexes.Add(sex);
            }
        }
        else
        {
            sexes.Add(Sex.Unsexed);
        }

        // add button for each sex
        foreach (var sex in sexes)
        {
            SexButton.AddItem(Loc.GetString($"humanoid-profile-editor-sex-{sex.ToString().ToLower()}-text"), (int)sex);
        }

        if (sexes.Contains(Profile.Sex))
            SexButton.SelectId((int)Profile.Sex);
        else
            SexButton.SelectId((int)sexes[0]);
    }

    private void UpdateEyePickers()
    {
        if (Profile == null)
        {
            return;
        }

        _markingsModel.SetOrganEyeColor(Profile.Appearance.EyeColor);
        EyeColorPicker.SetData(Profile.Appearance.EyeColor);
    }

    private void UpdateSkinColor()
    {
        if (Profile == null)
            return;

        var skin = _prototypeManager.Index<SpeciesPrototype>(Profile.Species).SkinColoration;
        var strategy = _prototypeManager.Index(skin).Strategy;
        _updatingSkinControls = true;

        try
        {
            switch (strategy.InputType)
            {
                case SkinColorationStrategyInput.Unary:
                    {
                        if (!Skin.Visible)
                        {
                            Skin.Visible = true;
                            RgbSkinColorContainer.Visible = false;
                        }

                        Skin.Value = strategy.ToUnary(Profile.Appearance.SkinColor);

                        break;
                    }
                case SkinColorationStrategyInput.Color:
                    {
                        if (!RgbSkinColorContainer.Visible)
                        {
                            Skin.Visible = false;
                            RgbSkinColorContainer.Visible = true;
                        }

                        _rgbSkinColorSelector.Color = strategy.ClosestSkinColor(Profile.Appearance.SkinColor);

                        break;
                    }
            }
        }
        finally
        {
            _updatingSkinControls = false;
        }
    }

    private void UpdateSpawnPriorityControls()
    {
        if (Profile == null)
        {
            return;
        }

        SpawnPriorityButton.SelectId((int)Profile.SpawnPriority);
    }

    /// <summary>
    /// Refreshes the species selector.
    /// </summary>
    public void RefreshSpecies()
    {
        SpeciesButton.Clear();
        _species.Clear();

        _species.AddRange(_prototypeManager.EnumeratePrototypes<SpeciesPrototype>().Where(o => o.RoundStart));
        _species.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        var speciesIds = _species.Select(o => o.ID).ToList();

        for (var i = 0; i < _species.Count; i++)
        {
            var name = GetSpeciesDisplayName(_species[i]);
            SpeciesButton.AddItem(name, i);

            if (Profile?.Species.Equals(_species[i].ID) == true)
            {
                SpeciesButton.SelectId(i);
            }
        }

        // If our species isn't available then reset it to default.
        if (Profile != null)
        {
            if (!speciesIds.Contains(Profile.Species))
            {
                SetSpecies(HumanoidCharacterProfile.DefaultSpecies);
            }
        }
    }

    private void UpdateSpeciesSelection()
    {
        if (Profile == null)
            return;

        if (_species.Count == 0)
        {
            RefreshSpecies();
            return;
        }

        for (var i = 0; i < _species.Count; i++)
        {
            if (_species[i].ID != Profile.Species)
                continue;

            SpeciesButton.SelectId(i);
            UpdateSpeciesGuidebookIcon();
            return;
        }

        RefreshSpecies();
    }

    private void SetSpecies(string newSpecies)
    {
        if (!_prototypeManager.TryIndex<SpeciesPrototype>(newSpecies, out var speciesProto))
            return;

        if (!CanSelectSpecies(speciesProto))
        {
            RefreshSpecies();
            return;
        }

        Profile = Profile?.WithSpecies(newSpecies);
        if (Profile == null)
            return;

        _markingsModel.OrganProfileData = _markingManager.GetProfileData(Profile.Species, Profile.Sex, Profile.Appearance.SkinColor, Profile.Appearance.EyeColor);
        _markingsModel.OrganData = _markingManager.GetMarkingData(newSpecies);
        _markingsModel.Markings = Profile.Appearance.Markings;
        _markingsModel.ValidateMarkings();
        Profile = Profile.WithCharacterAppearance(Profile.Appearance.WithMarkings(_markingsModel.Markings));
        OnSkinColorOnValueChanged(); // Species may have special color prefs, make sure to update it.
        // In case there's job restrictions for the species
        RefreshJobs();
        // In case there's species restrictions for loadouts
        RefreshLoadouts();
        UpdateSexControls(); // update sex for new species
        UpdateSpeciesGuidebookIcon();
        ReloadPreview();
    }

    private bool CanSelectSpecies(SpeciesPrototype species)
    {
        var isAdmin = _playerManager.LocalSession != null && _adminManager.IsAdmin(_playerManager.LocalSession);
        var metaLevel = 0;
        HashSet<string>? completedAchievements = null;

        if (_metaProgress.TryGetCachedSnapshot(out WH40KMetaProgressSnapshot snapshot))
        {
            metaLevel = snapshot.Level;

            if (species.RequiredAchievements.Count > 0)
            {
                completedAchievements = snapshot.Achievements
                    .Where(entry => entry.Completed)
                    .Select(entry => entry.Id)
                    .ToHashSet(StringComparer.Ordinal);
            }
        }

        return SpeciesSelectionRequirements.IsUnlocked(species, isAdmin, metaLevel, completedAchievements);
    }

    private string GetSpeciesDisplayName(SpeciesPrototype species)
    {
        var name = Loc.GetString(species.Name);

        if (CanSelectSpecies(species))
            return name;

        if (species.AdminOnly && species.RequiredMetaLevel > 0)
        {
            return Loc.GetString("humanoid-profile-editor-species-entry-admin-level-locked",
                ("species", name),
                ("level", species.RequiredMetaLevel));
        }

        if (species.AdminOnly)
            return Loc.GetString("humanoid-profile-editor-species-entry-admin-only", ("species", name));

        if (species.RequiredMetaLevel > 0)
        {
            return Loc.GetString("humanoid-profile-editor-species-entry-level-locked",
                ("species", name),
                ("level", species.RequiredMetaLevel));
        }

        return Loc.GetString("humanoid-profile-editor-species-entry-locked", ("species", name));
    }

    private void SetAge(int newAge)
    {
        Profile = Profile?.WithAge(newAge);
        ReloadPreview();
    }

    private void SetSex(Sex newSex)
    {
        Profile = Profile?.WithSex(newSex);
        // for convenience, default to most common gender when new sex is selected
        switch (newSex)
        {
            case Sex.Male:
                Profile = Profile?.WithGender(Gender.Male);
                break;
            case Sex.Female:
                Profile = Profile?.WithGender(Gender.Female);
                break;
            default:
                Profile = Profile?.WithGender(Gender.Epicene);
                break;
        }

        UpdateGenderControls();
        _markingsModel.SetOrganSexes(newSex);
        ReloadPreview();
    }

    private void SetGender(Gender newGender)
    {
        Profile = Profile?.WithGender(newGender);
        ReloadPreview();
    }

    private void SetVoiceTone(VoiceTone newVoiceTone)
    {
        Profile = Profile?.WithVoiceTone(newVoiceTone);
        ReloadProfilePreview();
    }

    private void SetSpawnPriority(SpawnPriorityPreference newSpawnPriority)
    {
        Profile = Profile?.WithSpawnPriorityPreference(newSpawnPriority);
        SetDirty();
    }

    private void OnSpeciesInfoButtonPressed(BaseButton.ButtonEventArgs args)
    {
        // TODO GUIDEBOOK
        // make the species guide book a field on the species prototype.
        // I.e., do what jobs/antags do.

        var guidebookController = UserInterfaceManager.GetUIController<GuidebookUIController>();
        var species = Profile?.Species ?? HumanoidCharacterProfile.DefaultSpecies;
        var page = DefaultSpeciesGuidebook;
        if (_prototypeManager.HasIndex<GuideEntryPrototype>(species))
            page = new ProtoId<GuideEntryPrototype>(species.Id); // Gross. See above todo comment.

        if (_prototypeManager.Resolve(DefaultSpeciesGuidebook, out var guideRoot))
        {
            var dict = new Dictionary<ProtoId<GuideEntryPrototype>, GuideEntry>();
            dict.Add(DefaultSpeciesGuidebook, guideRoot);
            //TODO: Don't close the guidebook if its already open, just go to the correct page
            guidebookController.OpenGuidebook(dict, includeChildren: true, selected: page);
        }
    }

    private void OnSkinColorOnValueChanged()
    {
        if (Profile is null || _updatingSkinControls)
            return;

        var skin = _prototypeManager.Index<SpeciesPrototype>(Profile.Species).SkinColoration;
        var strategy = _prototypeManager.Index(skin).Strategy;

        switch (strategy.InputType)
        {
            case SkinColorationStrategyInput.Unary:
                {
                    if (!Skin.Visible)
                    {
                        Skin.Visible = true;
                        RgbSkinColorContainer.Visible = false;
                    }

                    var color = strategy.FromUnary(Skin.Value);

                    _markingsModel.SetOrganSkinColor(color);
                    Profile = Profile.WithCharacterAppearance(Profile.Appearance.WithSkinColor(color));

                    break;
                }
            case SkinColorationStrategyInput.Color:
                {
                    if (!RgbSkinColorContainer.Visible)
                    {
                        Skin.Visible = false;
                        RgbSkinColorContainer.Visible = true;
                    }

                    var color = strategy.ClosestSkinColor(_rgbSkinColorSelector.Color);

                    _markingsModel.SetOrganSkinColor(color);
                    Profile = Profile.WithCharacterAppearance(Profile.Appearance.WithSkinColor(color));

                    break;
                }
        }

        ReloadProfilePreview();
    }
}
