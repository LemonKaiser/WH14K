using System.Linq;
using System.Numerics;
using Content.Client.Lobby;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Client._WH40K.Command;
using Content.Client._WH40K.Interface;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Whitelist;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Construction.UI
{
    /// <summary>
    /// This class presents the Construction/Crafting UI to the client, linking the <see cref="ConstructionSystem" /> with the
    /// model. This is where the bulk of UI work is done, either calling functions in the model to change state, or collecting
    /// data out of the model to *present* to the screen though the UI framework.
    /// </summary>
    internal sealed class ConstructionMenuPresenter : IDisposable
    {
        [Dependency] private readonly EntityManager _entManager = default!;
        [Dependency] private readonly IEntitySystemManager _systemManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IPlacementManager _placementManager = default!;
        [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IClientPreferencesManager _preferencesManager = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

        private readonly SpriteSystem _spriteSystem;
        private readonly ISawmill _sawmill;

        private readonly IConstructionMenuView _constructionView;
        private readonly EntityWhitelistSystem _whitelistSystem;

        private ConstructionSystem? _constructionSystem;
        private ConstructionPrototype? _selected;
        private List<ConstructionPrototype> _favoritedRecipes = [];
        private readonly Dictionary<string, ContainerButton> _recipeButtons = new();
        private readonly Dictionary<ContainerButton, (Label Title, Label Meta)> _gridButtonLabels = new();
        private string _selectedCategory = string.Empty;
        private (string Search, string Category) _lastPopulateArgs = (string.Empty, string.Empty);
        private bool _guideRefreshPending;

        private const string FavoriteCatName = "construction-category-favorites";
        private const string ForAllCategoryName = "construction-category-all";
        private const string Wh40KRecipePrefix = "WH40K";

        private bool CraftingAvailable
        {
            get => _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Visible;
            set
            {
                _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Visible = value;
                if (!value)
                    _constructionView.Close();
            }
        }

        /// <summary>
        /// Does the window have focus? If the window is closed, this will always return false.
        /// </summary>
        private bool IsAtFront => _constructionView.IsOpen && _constructionView.IsAtFront();

        private bool WindowOpen
        {
            get => _constructionView.IsOpen;
            set
            {
                if (value && CraftingAvailable)
                {
                    if (_constructionView.IsOpen)
                        _constructionView.MoveToFront();
                    else
                        _constructionView.OpenCentered();

                    if (_guideRefreshPending)
                    {
                        _guideRefreshPending = false;
                        OnViewPopulateRecipes(_constructionView, _lastPopulateArgs);
                    }

                    if (_selected != null)
                        PopulateInfo(_selected);
                }
                else
                    _constructionView.Close();
            }
        }

        /// <summary>
        /// Constructs a new instance of <see cref="ConstructionMenuPresenter" />.
        /// </summary>
        public ConstructionMenuPresenter()
        {
            // This is a lot easier than a factory
            IoCManager.InjectDependencies(this);
            _constructionView = new ConstructionMenu();
            _whitelistSystem = _entManager.System<EntityWhitelistSystem>();
            _spriteSystem = _entManager.System<SpriteSystem>();
            _sawmill = _logManager.GetSawmill("construction.ui");

            // This is required so that if we load after the system is initialized, we can bind to it immediately
            if (_systemManager.TryGetEntitySystem<ConstructionSystem>(out var constructionSystem))
                SystemBindingChanged(constructionSystem);

            _systemManager.SystemLoaded += OnSystemLoaded;
            _systemManager.SystemUnloaded += OnSystemUnloaded;

            _placementManager.PlacementChanged += OnPlacementChanged;

            _constructionView.OnClose +=
                () => _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Pressed = false;
            _constructionView.ClearAllGhosts += (_, _) => _constructionSystem?.ClearAllGhosts();
            _constructionView.PopulateRecipes += OnViewPopulateRecipes;
            _constructionView.RecipeSelected += OnViewRecipeSelected;
            _constructionView.BuildButtonToggled += (_, b) => BuildButtonToggled(b);
            _constructionView.EraseButtonToggled += (_, b) =>
            {
                if (_constructionSystem is null)
                    return;
                if (b)
                    _placementManager.Clear();
                _placementManager.ToggleEraserHijacked(new ConstructionPlacementHijack(_constructionSystem, null));
                _constructionView.EraseButtonPressed = b;
            };

            _constructionView.RecipeFavorited += (_, _) => OnViewFavoriteRecipe();

            SetFavorites(_preferencesManager.Preferences?.ConstructionFavorites ?? []);
            OnViewPopulateRecipes(_constructionView, (string.Empty, string.Empty));
        }

        public void OnHudCraftingButtonToggled(BaseButton.ButtonToggledEventArgs args)
        {
            WindowOpen = args.Pressed;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _constructionView.Dispose();

            SystemBindingChanged(null);
            _systemManager.SystemLoaded -= OnSystemLoaded;
            _systemManager.SystemUnloaded -= OnSystemUnloaded;

            _placementManager.PlacementChanged -= OnPlacementChanged;
        }

        private void OnPlacementChanged(object? sender, EventArgs e)
        {
            _constructionView.ResetPlacement();
        }

        private void OnViewRecipeSelected(object? sender, ConstructionMenu.ConstructionMenuListData? item)
        {
            if (item is null)
            {
                _selected = null;
                _constructionView.ClearRecipeInfo();
                return;
            }

            _selected = item.Prototype;

            if (_placementManager is { IsActive: true, Eraser: false })
                UpdateGhostPlacement();

            PopulateInfo(_selected);
        }

        private void OnGridViewRecipeSelected(object? _, ConstructionPrototype? recipe)
        {
            if (recipe is null)
            {
                _selected = null;
                _constructionView.ClearRecipeInfo();
                return;
            }

            _selected = recipe;

            if (_placementManager is { IsActive: true, Eraser: false })
                UpdateGhostPlacement();

            PopulateInfo(_selected);
        }

        private void OnViewPopulateRecipes(object? sender, (string search, string catagory) args)
        {
            if (_constructionSystem is null)
                return;

            _lastPopulateArgs = (args.search, args.catagory);
            ApplyViewThemeForCurrentTeam();
            var actualRecipes = GetAndSortRecipes(args);
            var category = args.catagory;
            var isEmptyCategory = string.IsNullOrEmpty(category) || category == ForAllCategoryName;

            var recipesList = _constructionView.Recipes;
            var recipesGrid = _constructionView.RecipesGrid;
            _recipeButtons.Clear();
            _gridButtonLabels.Clear();
            recipesGrid.RemoveAllChildren();

            _constructionView.RecipesGridScrollContainer.Visible = _constructionView.GridViewButtonPressed;
            _constructionView.Recipes.Visible = !_constructionView.GridViewButtonPressed;

            if (actualRecipes.Count == 0)
            {
                _selected = null;
                _constructionView.ClearRecipeInfo();
            }

            _constructionView.SetCatalogSummary(
                actualRecipes.Count,
                ResolveCategoryDisplayName(category, isEmptyCategory),
                _constructionView.GridViewButtonPressed);

            if (_constructionView.GridViewButtonPressed)
            {
                recipesList.PopulateList([]);
                PopulateGrid(recipesGrid, actualRecipes);
                if (_constructionView is ConstructionMenu menu)
                    menu.RefreshRecipeGridLayout();
            }
            else
            {
                recipesList.PopulateList(actualRecipes);
                if (_constructionView is ConstructionMenu menu)
                    menu.RefreshRecipeListSelectionTheme();
            }
        }

        private void PopulateGrid(GridContainer recipesGrid,
            IEnumerable<ConstructionMenu.ConstructionMenuListData> actualRecipes)
        {
            var visualIndex = 0;
            foreach (var recipe in actualRecipes)
            {
                var protoView = new EntityPrototypeView()
                {
                    Scale = new Vector2(1.1f),
                    Stretch = SpriteView.StretchMode.Fill,
                    SetSize = new Vector2(42f),
                };
                protoView.SetPrototype(recipe.TargetPrototype);

                var titleLabel = new Label
                {
                    Text = recipe.Prototype.Name,
                    HorizontalAlignment = Control.HAlignment.Center,
                    Align = Label.AlignMode.Center,
                    ClipText = true,
                    MaxWidth = 124
                };

                var metaLabel = new Label
                {
                    Text = BuildRecipeMetaTextDisplay(recipe.Prototype, recipe.StepCount),
                    HorizontalAlignment = Control.HAlignment.Center,
                    Align = Label.AlignMode.Center,
                    ClipText = true,
                    MaxWidth = 124,
                    StyleClasses = { "LabelSubText" }
                };

                var content = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    SeparationOverride = 3,
                    HorizontalExpand = true,
                    VerticalExpand = false,
                    HorizontalAlignment = Control.HAlignment.Center,
                    RectClipContent = true
                };
                content.AddChild(new Control { MinSize = new Vector2(0, 1) });
                content.AddChild(protoView);
                content.AddChild(titleLabel);
                content.AddChild(metaLabel);

                var itemButton = new ContainerButton()
                {
                    HorizontalExpand = true,
                    VerticalExpand = false,
                    VerticalAlignment = Control.VAlignment.Center,
                    Name = recipe.Prototype.Name,
                    ToolTip = recipe.Prototype.Name,
                    ToggleMode = true,
                    RectClipContent = true,
                    Children = { content },
                };

                var itemButtonPanelContainer = new PanelContainer
                {
                    HorizontalExpand = false,
                    VerticalExpand = false,
                    PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                        WH40KCommandUiStyles.ResolveCardBackground(ResolveCurrentTheme().ChaosTheme),
                        WH40KCommandUiStyles.ResolveMutedBorder(ResolveCurrentTheme().ChaosTheme)),
                    RectClipContent = true,
                    Children = { itemButton },
                    };

                itemButton.OnToggled += buttonToggledEventArgs =>
                {
                    SelectGridButton(itemButton, buttonToggledEventArgs.Pressed);

                    if (buttonToggledEventArgs.Pressed &&
                        _selected != null &&
                        _recipeButtons.TryGetValue(_selected.ID, out var oldButton))
                    {
                        oldButton.Pressed = false;
                        SelectGridButton(oldButton, false);
                    }

                    OnGridViewRecipeSelected(this, buttonToggledEventArgs.Pressed ? recipe.Prototype : null);
                };

                recipesGrid.AddChild(itemButtonPanelContainer);
                _recipeButtons[recipe.Prototype.ID] = itemButton;
                _gridButtonLabels[itemButton] = (titleLabel, metaLabel);
                var isCurrentButtonSelected = _selected == recipe.Prototype;
                itemButton.Pressed = isCurrentButtonSelected;
                SelectGridButton(itemButton, isCurrentButtonSelected);
                visualIndex++;
            }
        }

        private List<ConstructionMenu.ConstructionMenuListData> GetAndSortRecipes((string, string) args)
        {
            var recipes = new List<ConstructionMenu.ConstructionMenuListData>();

            var (search, category) = args;
            var isEmptyCategory = string.IsNullOrEmpty(category) || category == ForAllCategoryName;
            _selectedCategory = isEmptyCategory ? string.Empty : category;

            foreach (var recipe in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                if (recipe.Hide)
                    continue;

                if (_playerManager.LocalSession == null
                    || _playerManager.LocalEntity == null
                    || _whitelistSystem.IsWhitelistFail(recipe.EntityWhitelist, _playerManager.LocalEntity.Value))
                    continue;

                if (!IsRecipeAvailableForCurrentTeam(recipe))
                    continue;

                if (!_constructionSystem!.TryGetRecipePrototype(recipe.ID, out var targetProtoId))
                {
                    _sawmill.Error("Cannot find the target prototype in the recipe cache with the id \"{0}\" of {1}.",
                        recipe.ID,
                        nameof(ConstructionPrototype));
                    continue;
                }

                // WH40K branch policy: keep construction menu focused only on WH40K-defined recipes.
                if (!IsWh40KRecipe(recipe, targetProtoId))
                    continue;

                if (!string.IsNullOrEmpty(search) && (recipe.Name is { } name &&
                                                      !name.Contains(search.Trim(),
                                                          StringComparison.InvariantCultureIgnoreCase)))
                    continue;

                if (!isEmptyCategory)
                {
                    if ((category != FavoriteCatName || !_favoritedRecipes.Contains(recipe)) &&
                        recipe.Category != category)
                        continue;
                }

                var previewProtoId = ResolveMenuTargetPrototype(recipe, targetProtoId);

                if (!_prototypeManager.TryIndex(previewProtoId, out EntityPrototype? proto))
                    continue;

                var guide = _constructionSystem.GetGuide(recipe);
                recipes.Add(new(recipe, proto, guide?.Entries.Length ?? 0));
            }

            recipes.Sort(
                (a, b) => string.Compare(a.Prototype.Name, b.Prototype.Name, StringComparison.InvariantCulture));

            return recipes;
        }

        private void SelectGridButton(BaseButton button, bool select)
        {
            if (button.Parent is not PanelContainer buttonPanel)
                return;

            var (accent, chaosTheme) = ResolveCurrentTheme();
            buttonPanel.PanelOverride = select
                ? WH40KCommandUiStyles.CreateCardStyle(
                    WH40KCommandUiStyles.ResolveCardBackgroundAlt(chaosTheme),
                    accent)
                : WH40KCommandUiStyles.CreateCardStyle(
                    WH40KCommandUiStyles.ResolveCardBackground(chaosTheme),
                    WH40KCommandUiStyles.ResolveMutedBorder(chaosTheme));

            if (button is ContainerButton containerButton &&
                _gridButtonLabels.TryGetValue(containerButton, out var labels))
            {
                labels.Title.ModulateSelfOverride = select
                    ? Color.White
                    : WH40KCommandUiStyles.ResolveSoftText(chaosTheme);
                labels.Meta.ModulateSelfOverride = select
                    ? WH40KCommandUiStyles.ResolveSoftText(chaosTheme)
                    : WH40KCommandUiStyles.ResolveMutedText(chaosTheme);
            }
        }

        private void PopulateCategories(string? selectCategory = null)
        {
            var uniqueCategories = new HashSet<string>();

            foreach (var prototype in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                if (!IsWh40KRecipe(prototype))
                    continue;

                if (!IsRecipeAvailableForCurrentTeam(prototype))
                    continue;

                var category = prototype.Category;

                if (!string.IsNullOrEmpty(category))
                    uniqueCategories.Add(category);
            }

            var isFavorites = _favoritedRecipes.Count > 0;
            var categoriesArray = new string[isFavorites ? uniqueCategories.Count + 2 : uniqueCategories.Count + 1];

            var idx = 0;
            categoriesArray[idx++] = ForAllCategoryName;

            if (isFavorites)
                categoriesArray[idx++] = FavoriteCatName;

            var sortedProtoCategories = uniqueCategories.OrderBy(Loc.GetString);

            foreach (var cat in sortedProtoCategories)
            {
                categoriesArray[idx++] = cat;
            }

            _constructionView.OptionCategories.Clear();

            for (var i = 0; i < categoriesArray.Length; i++)
            {
                _constructionView.OptionCategories.AddItem(Loc.GetString(categoriesArray[i]), i);

                if (!string.IsNullOrEmpty(selectCategory) && selectCategory == categoriesArray[i])
                    _constructionView.OptionCategories.SelectId(i);
            }

            if (_constructionView.OptionCategories.SelectedId < 0)
                _constructionView.OptionCategories.SelectId(0);

            _constructionView.Categories = categoriesArray;
        }

        private static bool IsWh40KRecipe(ConstructionPrototype recipe)
        {
            return recipe.ID.StartsWith(Wh40KRecipePrefix, StringComparison.OrdinalIgnoreCase)
                   || recipe.Graph.ToString().StartsWith(Wh40KRecipePrefix, StringComparison.OrdinalIgnoreCase)
                   || recipe.Category.Contains("wh40k", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWh40KRecipe(ConstructionPrototype recipe, EntProtoId targetProtoId)
        {
            return IsWh40KRecipe(recipe)
                   || targetProtoId.ToString().StartsWith(Wh40KRecipePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsRecipeAvailableForCurrentTeam(ConstructionPrototype recipe)
        {
            if (recipe.WH40KAllowedTeams.Count == 0)
                return true;

            if (!_systemManager.TryGetEntitySystem<WH40KInterfaceThemeSystem>(out var themeSystem))
                return false;

            return recipe.IsWh40KTeamAllowed(themeSystem.CurrentTeamId);
        }

        private void PopulateInfo(ConstructionPrototype? prototype)
        {
            if (_constructionSystem is null)
                return;

            ApplyViewThemeForCurrentTeam();
            _constructionView.ClearRecipeInfo();

            if (prototype is null)
                return;

            if (!_constructionSystem.TryGetRecipePrototype(prototype.ID, out var targetProtoId))
                return;

            var previewProtoId = ResolveMenuTargetPrototype(prototype, targetProtoId);

            if (!_prototypeManager.TryIndex(previewProtoId, out EntityPrototype? proto))
                return;

            var guide = _constructionSystem.GetGuide(prototype);

            _constructionView.SetRecipeInfo(
                prototype.Name!,
                prototype.Description!,
                proto,
                prototype.Type != ConstructionType.Item,
                !_favoritedRecipes.Contains(prototype),
                ResolveCategoryDisplayName(prototype.Category, string.IsNullOrWhiteSpace(prototype.Category)),
                guide?.Entries.Length ?? 0);

            var stepList = _constructionView.RecipeStepList;
            GenerateStepList(prototype, stepList);
        }

        private void GenerateStepList(ConstructionPrototype prototype, ItemList stepList)
        {
            if (_constructionSystem?.GetGuide(prototype) is not { } guide)
                return;

            foreach (var entry in guide.Entries)
            {
                var text = entry.Arguments != null
                    ? Loc.GetString(entry.Localization, entry.Arguments)
                    : Loc.GetString(entry.Localization);

                if (entry.EntryNumber is { } number)
                {
                    text = Loc.GetString("construction-presenter-step-wrapper",
                        ("step-number", number),
                        ("text", text));
                }

                // The padding needs to be applied regardless of text length... (See PadLeft documentation)
                text = text.PadLeft(text.Length + entry.Padding);

                var icon = entry.Icon != null ? _spriteSystem.Frame0(entry.Icon) : Texture.Transparent;
                stepList.AddItem(text, icon, false);
            }
        }

        private void BuildButtonToggled(bool pressed)
        {
            if (pressed)
            {
                if (_selected == null)
                    return;

                // not bound to a construction system
                if (_constructionSystem is null)
                {
                    _constructionView.BuildButtonPressed = false;
                    return;
                }

                if (_selected.Type == ConstructionType.Item)
                {
                    _constructionSystem.TryStartItemConstruction(_selected.ID);
                    _constructionView.BuildButtonPressed = false;
                    return;
                }

                _placementManager.BeginPlacing(new PlacementInformation
                    {
                        IsTile = false,
                        PlacementOption = _selected.PlacementMode
                    },
                    new ConstructionPlacementHijack(_constructionSystem, _selected));

                UpdateGhostPlacement();
            }
            else
                _placementManager.Clear();

            _constructionView.BuildButtonPressed = pressed;
        }

        private void UpdateGhostPlacement()
        {
            if (_selected == null)
                return;

            if (_selected.Type != ConstructionType.Structure)
            {
                _placementManager.Clear();
                return;
            }

            var constructSystem = _systemManager.GetEntitySystem<ConstructionSystem>();

            _placementManager.BeginPlacing(new PlacementInformation()
                {
                    IsTile = false,
                    PlacementOption = _selected.PlacementMode,
                },
                new ConstructionPlacementHijack(constructSystem, _selected));

            _constructionView.BuildButtonPressed = true;
        }

        private void ApplyViewThemeForCurrentTeam()
        {
            if (_constructionView is not ConstructionMenu menu)
                return;

            var teamId = _systemManager.TryGetEntitySystem<WH40KInterfaceThemeSystem>(out var themeSystem)
                ? themeSystem.CurrentTeamId
                : null;

            menu.ApplyWh40KTheme(teamId);
        }

        private EntProtoId ResolveMenuTargetPrototype(ConstructionPrototype recipe, EntProtoId defaultTargetProtoId)
        {
            if (!_systemManager.TryGetEntitySystem<WH40KInterfaceThemeSystem>(out var themeSystem) ||
                !string.Equals(themeSystem.CurrentTeamId, "Heretics", StringComparison.OrdinalIgnoreCase))
            {
                return defaultTargetProtoId;
            }

            return recipe.ID switch
            {
                "WH40KStrategicResourcePointT1" => "WH40KStrategicPointResourceT1PreviewHeretics",
                "WH40KStrategicResearchPointT1" => "WH40KStrategicPointResearchT1PreviewHeretics",
                "WH40KStrategicInfluencePointT1" => "WH40KStrategicPointInfluenceT1PreviewHeretics",
                _ => defaultTargetProtoId
            };
        }

        private (Color Accent, bool ChaosTheme) ResolveCurrentTheme()
        {
            var teamId = _systemManager.TryGetEntitySystem<WH40KInterfaceThemeSystem>(out var themeSystem)
                ? themeSystem.CurrentTeamId
                : null;
            var accent = string.IsNullOrWhiteSpace(teamId)
                ? WH40KCommandUiStyles.DefaultAccent
                : WH40KTeamIdentityClientResolver.ResolveAccentColor(teamId, WH40KCommandUiStyles.DefaultAccent);
            var chaosTheme = !string.IsNullOrWhiteSpace(teamId) &&
                             WH40KTeamIdentityClientResolver.UsesHereticsDoctrinePresentation(teamId);
            return (accent, chaosTheme);
        }

        private static string ResolveCategoryDisplayName(string? category, bool fallbackToAll = false)
        {
            if (fallbackToAll)
                return Loc.GetString(ForAllCategoryName);

            return string.IsNullOrWhiteSpace(category)
                ? Loc.GetString("construction-category-misc")
                : Loc.GetString(category);
        }

        private static string BuildRecipeMetaTextDisplay(ConstructionPrototype prototype, int stepCount)
        {
            var category = ResolveCategoryDisplayName(prototype.Category);
            return Loc.GetString("construction-menu-list-meta", ("category", category), ("count", stepCount));
        }

        private static string BuildRecipeMetaText(ConstructionPrototype prototype, int stepCount)
        {
            var category = ResolveCategoryDisplayName(prototype.Category);
            return Loc.GetString("construction-menu-list-meta", ("category", category), ("count", stepCount));
        }

        private void OnSystemLoaded(object? sender, SystemChangedArgs args)
        {
            if (args.System is ConstructionSystem system)
                SystemBindingChanged(system);
        }

        private void OnSystemUnloaded(object? sender, SystemChangedArgs args)
        {
            if (args.System is ConstructionSystem)
                SystemBindingChanged(null);
        }

        private void OnViewFavoriteRecipe()
        {
            if (_selected is null)
                return;

            if (!_favoritedRecipes.Remove(_selected))
                _favoritedRecipes.Add(_selected);

            if (_selectedCategory == FavoriteCatName)
            {
                OnViewPopulateRecipes(_constructionView,
                    _favoritedRecipes.Count > 0 ? (string.Empty, FavoriteCatName) : (string.Empty, string.Empty));
            }

            var newFavorites = new List<ProtoId<ConstructionPrototype>>(_favoritedRecipes.Count);
            foreach (var recipe in _favoritedRecipes)
                newFavorites.Add(recipe.ID);

            _preferencesManager.UpdateConstructionFavorites(newFavorites);
            PopulateInfo(_selected);
            PopulateCategories(_selectedCategory);
        }

        public void SetFavorites(IReadOnlyList<ProtoId<ConstructionPrototype>> favorites)
        {
            _favoritedRecipes.Clear();

            foreach (var id in favorites)
            {
                if (_prototypeManager.TryIndex(id, out ConstructionPrototype? recipe))
                    _favoritedRecipes.Add(recipe);
            }

            if (_selectedCategory == FavoriteCatName)
            {
                OnViewPopulateRecipes(_constructionView,
                    _favoritedRecipes.Count > 0 ? (string.Empty, FavoriteCatName) : (string.Empty, string.Empty));
            }

            PopulateCategories(_selectedCategory);
        }

        private void SystemBindingChanged(ConstructionSystem? newSystem)
        {
            if (newSystem is null)
            {
                if (_constructionSystem is null)
                    return;

                UnbindFromSystem();
            }
            else
            {
                if (_constructionSystem is null)
                {
                    BindToSystem(newSystem);
                    return;
                }

                UnbindFromSystem();
                BindToSystem(newSystem);
            }
        }

        private void BindToSystem(ConstructionSystem system)
        {
            _constructionSystem = system;

            OnViewPopulateRecipes(_constructionView, (string.Empty, string.Empty));

            system.ToggleCraftingWindow += SystemOnToggleMenu;
            system.FlipConstructionPrototype += SystemFlipConstructionPrototype;
            system.CraftingAvailabilityChanged += SystemCraftingAvailabilityChanged;
            system.ConstructionGuideAvailable += SystemGuideAvailable;
            if (_uiManager.GetActiveUIWidgetOrNull<GameTopMenuBar>() != null)
            {
                CraftingAvailable = system.CraftingEnabled;
            }
        }

        private void UnbindFromSystem()
        {
            var system = _constructionSystem;

            if (system is null)
                throw new InvalidOperationException();

            system.ToggleCraftingWindow -= SystemOnToggleMenu;
            system.FlipConstructionPrototype -= SystemFlipConstructionPrototype;
            system.CraftingAvailabilityChanged -= SystemCraftingAvailabilityChanged;
            system.ConstructionGuideAvailable -= SystemGuideAvailable;
            _constructionSystem = null;
        }

        private void SystemCraftingAvailabilityChanged(object? sender, CraftingAvailabilityChangedArgs e)
        {
            if (_uiManager.ActiveScreen == null)
                return;
            CraftingAvailable = e.Available;
        }

        private void SystemOnToggleMenu(object? sender, EventArgs eventArgs)
        {
            if (!CraftingAvailable)
                return;

            if (WindowOpen)
            {
                if (IsAtFront)
                {
                    WindowOpen = false;
                    _uiManager.GetActiveUIWidget<GameTopMenuBar>()
                        .CraftingButton.SetClickPressed(false); // This does not call CraftingButtonToggled
                }
                else
                    _constructionView.MoveToFront();
            }
            else
            {
                WindowOpen = true;
                _uiManager.GetActiveUIWidget<GameTopMenuBar>()
                    .CraftingButton.SetClickPressed(true); // This does not call CraftingButtonToggled
            }
        }

        private void SystemFlipConstructionPrototype(object? sender, EventArgs eventArgs)
        {
            if (!_placementManager.IsActive || _placementManager.Eraser)
            {
                return;
            }

            if (_selected == null || _selected.Mirror == null)
            {
                return;
            }

            _selected = _prototypeManager.Index<ConstructionPrototype>(_selected.Mirror);
            UpdateGhostPlacement();
        }

        private void SystemGuideAvailable(object? sender, string e)
        {
            if (!CraftingAvailable)
                return;

            if (!WindowOpen)
            {
                _guideRefreshPending = true;
                return;
            }

            OnViewPopulateRecipes(_constructionView, _lastPopulateArgs);

            if (_selected == null)
                return;

            if (string.Equals(_selected.ID, e, StringComparison.Ordinal))
                PopulateInfo(_selected);
        }
    }
}
