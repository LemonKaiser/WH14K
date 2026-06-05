using System.Linq;
using Content.Client.Guidebook;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared.CCVar;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby;

public sealed partial class LobbyUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>
{
    [Dependency] private IClientPreferencesManager _preferencesManager = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IFileDialogManager _dialogManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IStateManager _stateManager = default!;
    [Dependency] private JobRequirementsManager _requirements = default!;
    [Dependency] private MarkingManager _markings = default!;
    [UISystemDependency] private readonly GuidebookSystem _guide = default!;

    private CharacterSetupGui? _characterSetup;
    private HumanoidProfileEditor? _profileEditor;
    private CharacterSetupGuiSavePanel? _savePanel;

    /// <summary>
    /// This is the characher preview panel in the chat. This should only update if their character updates.
    /// </summary>
    private LobbyCharacterPreviewPanel? PreviewPanel => GetLobbyPreview();

    /// <summary>
    /// This is the modified profile currently being edited.
    /// </summary>
    private HumanoidCharacterProfile? EditedProfile => _profileEditor?.Profile;

    private int? EditedSlot => _profileEditor?.CharacterSlot;

    public override void Initialize()
    {
        base.Initialize();
        _prototypeManager.PrototypesReloaded += OnProtoReload;
        _preferencesManager.OnServerDataLoaded += PreferencesDataLoaded;
        _requirements.Updated += OnRequirementsUpdated;

        _configurationManager.OnValueChanged(CCVars.FlavorText, args =>
        {
            _profileEditor?.RefreshFlavorText();
        });

        _configurationManager.OnValueChanged(CCVars.GameRoleTimers, _ => RefreshProfileEditor());
        _configurationManager.OnValueChanged(CCVars.GameRoleLoadoutTimers, _ => RefreshProfileEditor());

        _configurationManager.OnValueChanged(CCVars.GameRoleWhitelist, _ => RefreshProfileEditor());
    }

    private LobbyCharacterPreviewPanel? GetLobbyPreview()
    {
        if (_stateManager.CurrentState is LobbyState lobby)
        {
            return lobby.Lobby?.CharacterPreview;
        }

        return null;
    }

    private void OnRequirementsUpdated()
    {
        if (_profileEditor != null)
        {
            _profileEditor.RefreshAntags();
            _profileEditor.RefreshJobs();
        }
    }

    private void OnProtoReload(PrototypesReloadedEventArgs obj)
    {
        if (_profileEditor != null)
        {
            if (obj.WasModified<AntagPrototype>())
            {
                _profileEditor.RefreshAntags();
            }

            if (obj.WasModified<JobPrototype>() ||
                obj.WasModified<DepartmentPrototype>())
            {
                _profileEditor.RefreshJobs();
            }

            if (obj.WasModified<LoadoutPrototype>() ||
                obj.WasModified<LoadoutGroupPrototype>() ||
                obj.WasModified<RoleLoadoutPrototype>())
            {
                _profileEditor.RefreshLoadouts();
            }

            if (obj.WasModified<SpeciesPrototype>())
            {
                _profileEditor.RefreshSpecies();
            }

            if (obj.WasModified<TraitPrototype>())
            {
                _profileEditor.RefreshTraits();
            }
        }
    }

    private void PreferencesDataLoaded()
    {
        PreviewPanel?.SetLoaded(true);

        if (_stateManager.CurrentState is not LobbyState)
            return;

        RefreshLobbyPreview();
        RefreshCharacterSetupIfOpen();
    }

    public void OnStateEntered(LobbyState state)
    {
        if (state.Lobby?.CharacterPreview != null)
            state.Lobby.CharacterPreview.CharacterSelected += OnPreviewCharacterSelected;

        PreviewPanel?.SetLoaded(_preferencesManager.ServerDataLoaded);
        RefreshLobbyPreview();
        RefreshCharacterSetupIfOpen();
    }

    public void OnStateExited(LobbyState state)
    {
        if (state.Lobby?.CharacterPreview != null)
            state.Lobby.CharacterPreview.CharacterSelected -= OnPreviewCharacterSelected;

        PreviewPanel?.SetLoaded(false);

        if (_savePanel != null)
        {
            _savePanel.Close();
            _savePanel.Orphan();
            _savePanel = null;
        }

        if (_profileEditor != null)
        {
            _profileEditor.Orphan();
        }

        if (_characterSetup != null)
        {
            _characterSetup.Orphan();
        }

        _characterSetup = null;
        _profileEditor = null;
    }

    /// <summary>
    /// Reloads every single character setup control.
    /// </summary>
    public void ReloadCharacterSetup()
    {
        RefreshLobbyPreview();
        EnsureGui();
        RefreshCharacterSetupIfOpen();
    }

    public void RefreshLocalization()
    {
        RefreshLobbyPreview();
        _characterSetup?.Relocalize();
        _profileEditor?.Relocalize();
        _savePanel?.Relocalize();
    }

    /// <summary>
    /// Refreshes the character preview in the lobby chat.
    /// </summary>
    private void RefreshLobbyPreview(bool refreshCarousel = true)
    {
        if (PreviewPanel == null)
            return;

        if (refreshCarousel || !PreviewPanel.HasCharacters)
            PreviewPanel.SetCharacters(_preferencesManager.Preferences);
        else
            PreviewPanel.SetSelectedSlot(_preferencesManager.Preferences?.SelectedCharacterIndex);

        var (humanoid, _) = GetSelectedCharacterData();
        if (humanoid == null)
        {
            PreviewPanel.SetSummaryText(string.Empty);
            return;
        }

        PreviewPanel.SetSummaryText(humanoid.Summary);
    }

    private void OnPreviewCharacterSelected(int slot)
    {
        _preferencesManager.SelectCharacter(slot);
        RefreshLobbyPreview(refreshCarousel: false);
        RefreshCharacterSetupIfOpen(reloadPickers: false, fastProfileSwap: true);
    }

    private void RefreshCharacterSetupIfOpen(bool reloadPickers = true, bool reloadProfile = true, bool fastProfileSwap = false)
    {
        if (_characterSetup == null || _profileEditor == null)
            return;

        var (profile, slot) = GetSelectedCharacterData();

        if (reloadPickers)
            _characterSetup.ReloadCharacterPickers();
        else
            _characterSetup.SetSelectedCharacter(slot);

        if (!reloadProfile)
            return;

        _profileEditor.SetProfile(profile, slot, fullRefresh: !fastProfileSwap);
    }

    private (HumanoidCharacterProfile? Profile, int? Slot) GetSelectedCharacterData()
    {
        var preferences = _preferencesManager.Preferences;
        if (preferences == null)
            return (null, null);

        if (preferences.Characters.TryGetValue(preferences.SelectedCharacterIndex, out var selected))
            return (selected, preferences.SelectedCharacterIndex);

        foreach (var (slot, profile) in preferences.Characters.OrderBy(character => character.Key))
        {
            return (profile, slot);
        }

        return (null, null);
    }

    private void RefreshProfileEditor()
    {
        _profileEditor?.RefreshAntags();
        _profileEditor?.RefreshJobs();
        _profileEditor?.RefreshLoadouts();
    }

    private void SaveProfile()
    {
        DebugTools.Assert(EditedProfile != null);

        if (EditedProfile == null || EditedSlot == null)
            return;

        var selected = _preferencesManager.Preferences?.SelectedCharacterIndex;

        if (selected == null)
            return;

        _preferencesManager.UpdateCharacter(EditedProfile, EditedSlot.Value);
        ReloadCharacterSetup();
    }

    private void CloseProfileEditor()
    {
        if (_profileEditor == null)
            return;

        _profileEditor.SetProfile(null, null);
        _profileEditor.Visible = false;

        if (_stateManager.CurrentState is LobbyState lobbyGui)
        {
            lobbyGui.SwitchState(LobbyGui.LobbyGuiState.Default);
        }
    }

    private void OpenSavePanel()
    {
        if (_savePanel is { IsOpen: true })
            return;

        _savePanel = new CharacterSetupGuiSavePanel();

        _savePanel.SaveButton.OnPressed += _ =>
        {
            SaveProfile();

            _savePanel.Close();

            CloseProfileEditor();
        };

        _savePanel.NoSaveButton.OnPressed += _ =>
        {
            _savePanel.Close();

            CloseProfileEditor();
        };

        _savePanel.OpenCentered();
    }

    private (CharacterSetupGui, HumanoidProfileEditor) EnsureGui()
    {
        if (_characterSetup != null && _profileEditor != null)
        {
            _characterSetup.Visible = true;
            _profileEditor.Visible = true;
            return (_characterSetup, _profileEditor);
        }

        _profileEditor = new HumanoidProfileEditor(
            _preferencesManager,
            _configurationManager,
            EntityManager,
            _dialogManager,
            LogManager,
            _playerManager,
            _prototypeManager,
            _resourceCache,
            _requirements,
            _markings);

        _profileEditor.OnOpenGuidebook += _guide.OpenHelp;

        _characterSetup = new CharacterSetupGui(_profileEditor);

        _characterSetup.CloseButton.OnPressed += _ =>
        {
            // Open the save panel if we have unsaved changes.
            if (_profileEditor.Profile != null && _profileEditor.IsDirty)
            {
                OpenSavePanel();

                return;
            }

            // Reset sliders etc.
            CloseProfileEditor();
        };

        _profileEditor.Save += SaveProfile;

        _characterSetup.CreateCharacterRequested += () =>
        {
            _preferencesManager.CreateCharacter(HumanoidCharacterProfile.Random());
            RefreshLobbyPreview();
            RefreshCharacterSetupIfOpen(reloadPickers: true, reloadProfile: false);
        };

        _characterSetup.SelectCharacter += args =>
        {
            _preferencesManager.SelectCharacter(args);
            RefreshLobbyPreview(refreshCarousel: false);
            RefreshCharacterSetupIfOpen(reloadPickers: false, fastProfileSwap: true);
        };

        _characterSetup.DeleteCharacter += args =>
        {
            _preferencesManager.DeleteCharacter(args);
            RefreshLobbyPreview();

            if (EditedSlot == args)
            {
                RefreshCharacterSetupIfOpen();
            }
            else
            {
                RefreshCharacterSetupIfOpen(reloadPickers: true, reloadProfile: false);
            }
        };

        if (_stateManager.CurrentState is LobbyState lobby)
        {
            lobby.Lobby?.CharacterSetupState.AddChild(_characterSetup);
        }

        return (_characterSetup, _profileEditor);
    }
}
