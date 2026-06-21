using System.Linq;
using System.Numerics;
using Content.Client.Lobby.UI.Loadouts;
using Content.Client.Lobby.UI.Roles;
using Content.Client.Stylesheets;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private static readonly Color JobGroupHeaderBackground = Color.FromHex("#181B22");
    private static readonly Color JobGroupHeaderBorder = Color.FromHex("#6E5A2F");
    private static readonly Color JobGroupHeaderText = Color.FromHex("#E2C160");

    /// <summary>
    /// Temporary override of their selected job, used to preview roles.
    /// </summary>
    public JobPrototype? JobOverride;

    // One at a time.
    private LoadoutWindow? _loadoutWindow;
    private RoleLoadout? _activeLoadout;
    private ICommonSession? _activeLoadoutSession;
    private IDependencyCollection? _activeLoadoutCollection;

    private List<(string, RequirementsSelector)> _jobPriorities = new();

    private readonly Dictionary<string, BoxContainer> _jobCategories;

    /// <summary>
    /// Updates selected job priorities to the profile's.
    /// </summary>
    private void UpdateJobPriorities()
    {
        foreach (var (jobId, prioritySelector) in _jobPriorities)
        {
            var priority = (int)(Profile?.JobPriorities.GetValueOrDefault(jobId, JobPriority.Never) ?? JobPriority.Never);
            if (prioritySelector.Selected == priority)
                continue;

            prioritySelector.Select(priority);
        }
    }

    /// <summary>
    /// Refresh all loadouts.
    /// </summary>
    public void RefreshLoadouts()
    {
        CloseLoadoutWindow();
    }

    private void CloseLoadoutWindow()
    {
        _loadoutWindow?.Orphan();
        _loadoutWindow = null;
        _activeLoadout = null;
        _activeLoadoutSession = null;
        _activeLoadoutCollection = null;
    }

    private void OpenLoadout(
        JobPrototype? jobProto,
        RoleLoadout roleLoadout,
        RoleLoadoutPrototype roleLoadoutProto,
        LoadoutWindowSection section)
    {
        CloseLoadoutWindow();
        var collection = IoCManager.Instance;

        if (collection == null || _playerManager.LocalSession == null || Profile == null)
            return;

        JobOverride = jobProto;
        var session = _playerManager.LocalSession;
        _activeLoadout = roleLoadout;
        _activeLoadoutSession = session;
        _activeLoadoutCollection = collection;
        var titleLocKey = section == LoadoutWindowSection.Armament
            ? "loadout-window-title-armament"
            : "loadout-window-title-loadout";

        _loadoutWindow = new LoadoutWindow(
            Profile,
            roleLoadout,
            roleLoadoutProto,
            _playerManager.LocalSession,
            collection,
            section)
        {
            Title = Loc.GetString(titleLocKey, ("job", $"{jobProto?.LocalizedName}")),
        };

        // Refresh the buttons etc.
        _loadoutWindow.RefreshLoadouts(roleLoadout, session, collection);
        _loadoutWindow.OpenCenteredLeft();

        _loadoutWindow.OnNameChanged += name =>
        {
            roleLoadout.EntityName = name;
            Profile = Profile.WithLoadout(roleLoadout);
            SetDirty();
        };

        _loadoutWindow.OnLoadoutPressed += (loadoutGroup, loadoutProto) =>
        {
            roleLoadout.AddLoadout(loadoutGroup, loadoutProto, _prototypeManager);
            _loadoutWindow?.RefreshLoadouts(roleLoadout, session, collection);
            Profile = Profile?.WithLoadout(roleLoadout);
            SetDirty();
            ReloadPreview();
        };

        _loadoutWindow.OnLoadoutUnpressed += (loadoutGroup, loadoutProto) =>
        {
            roleLoadout.RemoveLoadout(loadoutGroup, loadoutProto, _prototypeManager);
            _loadoutWindow?.RefreshLoadouts(roleLoadout, session, collection);
            Profile = Profile?.WithLoadout(roleLoadout);
            SetDirty();
            ReloadPreview();
        };

        _loadoutWindow.OnWeaponModsChanged += (loadoutGroup, loadoutProto, slotId, modId) =>
        {
            // WH40K: selecting a weapon mod implies intent to use that loadout.
            // If the loadout isn't selected yet, auto-select it first so the mod has
            // a Loadout entry to attach its SelectedMods to. Without this, clicking a
            // mod on an un-selected loadout silently does nothing (no Loadout entry exists
            // to receive the SelectedMods update, so RefreshLoadouts resets the button).
            if (roleLoadout.SelectedLoadouts.TryGetValue(loadoutGroup, out var selections))
            {
                var existing = selections.FirstOrDefault(s => s.Prototype == loadoutProto);
                if (existing == null && modId != null)
                {
                    roleLoadout.AddLoadout(loadoutGroup, loadoutProto, _prototypeManager);
                    // Re-read after add so we mutate the actual stored entry.
                    selections = roleLoadout.SelectedLoadouts[loadoutGroup];
                    existing = selections.FirstOrDefault(s => s.Prototype == loadoutProto);
                }

                if (existing != null)
                {
                    if (modId == null)
                        existing.SelectedMods.Remove(slotId);
                    else
                        existing.SelectedMods[slotId] = modId;
                }
            }
            _loadoutWindow?.RefreshLoadouts(roleLoadout, session, collection);
            Profile = Profile?.WithLoadout(roleLoadout);
            SetDirty();
            ReloadPreview();
        };

        JobOverride = jobProto;
        ReloadPreview();

        _loadoutWindow.OnClose += () =>
        {
            _loadoutWindow = null;
            _activeLoadout = null;
            _activeLoadoutSession = null;
            _activeLoadoutCollection = null;
            JobOverride = null;
            ReloadPreview();
        };

        if (Profile is null)
            return;

        UpdateJobPriorities();
    }

    /// <summary>
    /// Refreshes all job selectors.
    /// </summary>
    public void RefreshJobs()
    {
        JobList.RemoveAllChildren();
        _jobCategories.Clear();
        _jobPriorities.Clear();
        var firstCategory = true;

        // Get all displayed departments
        var departments = new List<DepartmentPrototype>();
        foreach (var department in _prototypeManager.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (department.EditorHidden)
                continue;

            departments.Add(department);
        }

        departments.Sort(DepartmentUIComparer.Instance);

        var items = new[]
        {
                ("humanoid-profile-editor-job-priority-never-button", (int) JobPriority.Never),
                ("humanoid-profile-editor-job-priority-low-button", (int) JobPriority.Low),
                ("humanoid-profile-editor-job-priority-medium-button", (int) JobPriority.Medium),
                ("humanoid-profile-editor-job-priority-high-button", (int) JobPriority.High),
            };

        foreach (var department in departments)
        {
            var departmentName = Loc.GetString(department.Name);

            if (!_jobCategories.TryGetValue(department.ID, out var category))
            {
                category = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    Name = department.ID,
                    ToolTip = Loc.GetString("humanoid-profile-editor-jobs-amount-in-department-tooltip",
                        ("departmentName", departmentName))
                };

                if (firstCategory)
                {
                    firstCategory = false;
                }
                else
                {
                    category.AddChild(new Control
                    {
                        MinSize = new Vector2(0, 23),
                    });
                }

                category.AddChild(CreateJobCategoryHeader(departmentName));

                _jobCategories[department.ID] = category;
                JobList.AddChild(category);
            }

            var jobs = department.Roles.Select(jobId => _prototypeManager.Index(jobId))
                .Where(job => job.SetPreference)
                .ToArray();

            Array.Sort(jobs, JobUIComparer.Instance);

            foreach (var job in jobs)
            {
                var jobContainer = new BoxContainer()
                {
                    Orientation = LayoutOrientation.Horizontal,
                };
                Control? loadoutControl = null;

                var selector = new RequirementsSelector()
                {
                    Margin = new Thickness(3f, 3f, 3f, 0f),
                };
                selector.OnOpenGuidebook += OnOpenGuidebook;

                var icon = new TextureRect
                {
                    TextureScale = new Vector2(2, 2),
                    VerticalAlignment = VAlignment.Center
                };
                var jobIcon = _prototypeManager.Index(job.Icon);
                icon.Texture = _sprite.Frame0(jobIcon.Icon);
                selector.Setup(items, job.LocalizedName, 200, job.LocalizedDescription, icon, job.Guides);

                if (!_requirements.IsAllowed(job, Profile, out var reason))
                {
                    selector.LockRequirements(reason);
                }
                else
                {
                    selector.UnlockRequirements();
                }

                selector.OnSelected += selectedPrio =>
                {
                    var selectedJobPrio = (JobPriority)selectedPrio;
                    Profile = Profile?.WithJobPriority(job.ID, selectedJobPrio);

                    foreach (var (jobId, other) in _jobPriorities)
                    {
                        // Sync other selectors with the same job in case of multiple department jobs
                        if (jobId == job.ID)
                        {
                            other.Select(selectedPrio);
                            continue;
                        }

                        if (selectedJobPrio != JobPriority.High || (JobPriority)other.Selected != JobPriority.High)
                            continue;

                        // Lower any other high priorities to medium.
                        other.Select((int)JobPriority.Medium);
                        Profile = Profile?.WithJobPriority(jobId, JobPriority.Medium);
                    }

                    // TODO: Only reload on high change (either to or from).
                    ReloadPreview();

                    UpdateJobPriorities();
                    SetDirty();
                };

                var collection = IoCManager.Instance!;
                var protoManager = collection.Resolve<IPrototypeManager>();

                if (!protoManager.TryIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID), out var roleLoadoutProto))
                {
                    loadoutControl = CreateLoadoutButton("loadout-window", null, enabled: false);
                }
                else
                {
                    var hasEquipmentGroups = LoadoutWindow.HasVisibleGroups(roleLoadoutProto, protoManager, LoadoutWindowSection.Equipment);
                    var hasArmamentGroups = LoadoutWindow.HasVisibleGroups(roleLoadoutProto, protoManager, LoadoutWindowSection.Armament);

                    if (hasEquipmentGroups || hasArmamentGroups)
                    {
                        loadoutControl = CreateLoadoutButtonCluster(
                            job,
                            roleLoadoutProto,
                            hasEquipmentGroups,
                            hasArmamentGroups);
                    }
                }

                _jobPriorities.Add((job.ID, selector));
                jobContainer.AddChild(selector);
                if (loadoutControl != null)
                    jobContainer.AddChild(loadoutControl);
                category.AddChild(jobContainer);
            }
        }

        UpdateJobPriorities();
    }

    private PanelContainer CreateJobCategoryHeader(string departmentName)
    {
        return new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = JobGroupHeaderBackground,
                BorderColor = JobGroupHeaderBorder.WithAlpha(0.8f),
                BorderThickness = new Thickness(0f, 0f, 0f, 1f),
            },
            Margin = new Thickness(0f, 0f, 0f, 3f),
            Children =
            {
                new Label
                {
                    Text = Loc.GetString(
                        "humanoid-profile-editor-department-jobs-label",
                        ("departmentName", departmentName)),
                    Margin = new Thickness(5f, 0f, 0f, 0f),
                    FontColorOverride = JobGroupHeaderText,
                }
            }
        };
    }

    private Control CreateLoadoutButtonCluster(
        JobPrototype jobProto,
        RoleLoadoutPrototype roleLoadoutProto,
        bool hasEquipmentGroups,
        bool hasArmamentGroups)
    {
        if (hasEquipmentGroups && !hasArmamentGroups)
        {
            return CreateLoadoutButton(
                "loadout-window",
                () => OpenFilteredLoadout(jobProto, roleLoadoutProto, LoadoutWindowSection.Equipment));
        }

        if (hasArmamentGroups && !hasEquipmentGroups)
        {
            return CreateLoadoutButton(
                "loadout-window-armament",
                () => OpenFilteredLoadout(jobProto, roleLoadoutProto, LoadoutWindowSection.Armament));
        }

        var cluster = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(3f, 3f, 0f, 0f),
            SeparationOverride = 0,
        };

        cluster.AddChild(CreateLoadoutButton(
            "loadout-window",
            () => OpenFilteredLoadout(jobProto, roleLoadoutProto, LoadoutWindowSection.Equipment),
            StyleClass.ButtonOpenRight));

        cluster.AddChild(CreateLoadoutButton(
            "loadout-window-armament",
            () => OpenFilteredLoadout(jobProto, roleLoadoutProto, LoadoutWindowSection.Armament),
            StyleClass.ButtonOpenLeft));

        return cluster;
    }

    private Button CreateLoadoutButton(
        string locKey,
        Action? onPressed,
        string? styleClass = null,
        bool enabled = true)
    {
        var button = new Button
        {
            Text = Loc.GetString(locKey),
            Disabled = !enabled,
            HorizontalAlignment = HAlignment.Right,
            HorizontalExpand = false,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(3f, 3f, 0f, 0f),
        };

        if (!string.IsNullOrWhiteSpace(styleClass))
            button.AddStyleClass(styleClass);

        if (onPressed != null)
            button.OnPressed += _ => onPressed();

        return button;
    }

    private void OpenFilteredLoadout(
        JobPrototype jobProto,
        RoleLoadoutPrototype roleLoadoutProto,
        LoadoutWindowSection section)
    {
        RoleLoadout? loadout = null;

        Profile?.Loadouts.TryGetValue(LoadoutSystem.GetJobPrototype(jobProto.ID), out loadout);
        loadout = loadout?.Clone();

        if (loadout == null)
        {
            loadout = new RoleLoadout(roleLoadoutProto.ID);
            loadout.SetDefault(Profile, _playerManager.LocalSession, _prototypeManager);
        }

        OpenLoadout(jobProto, loadout, roleLoadoutProto, section);
    }

    public void RefreshAntags()
    {
        AntagList.RemoveAllChildren();
        var items = new[]
        {
            ("humanoid-profile-editor-antag-preference-yes-button", 0),
            ("humanoid-profile-editor-antag-preference-no-button", 1)
        };

        foreach (var antag in _prototypeManager.EnumeratePrototypes<AntagPrototype>().OrderBy(a => Loc.GetString(a.Name)))
        {
            if (!antag.SetPreference)
                continue;

            var antagContainer = new BoxContainer()
            {
                Orientation = LayoutOrientation.Horizontal,
            };

            var selector = new RequirementsSelector()
            {
                Margin = new Thickness(3f, 3f, 3f, 0f),
            };
            selector.OnOpenGuidebook += OnOpenGuidebook;

            var title = Loc.GetString(antag.Name);
            var description = Loc.GetString(antag.Objective);
            selector.Setup(items, title, 250, description, guides: antag.Guides);
            selector.Select(Profile?.AntagPreferences.Contains(antag.ID) == true ? 0 : 1);

            if (!_requirements.IsAllowed(
                    antag,
                    (HumanoidCharacterProfile?)_preferencesManager.Preferences?.SelectedCharacter,
                    out var reason))
            {
                selector.LockRequirements(reason);
                Profile = Profile?.WithAntagPreference(antag.ID, false);
                SetDirty();
            }
            else
            {
                selector.UnlockRequirements();
            }

            selector.OnSelected += preference =>
            {
                Profile = Profile?.WithAntagPreference(antag.ID, preference == 0);
                SetDirty();
            };

            antagContainer.AddChild(selector);

            antagContainer.AddChild(new Button()
            {
                Disabled = true,
                Text = Loc.GetString("loadout-window"),
                HorizontalAlignment = HAlignment.Right,
                Margin = new Thickness(3f, 0f, 0f, 0f),
            });

            AntagList.AddChild(antagContainer);
        }
    }
}
