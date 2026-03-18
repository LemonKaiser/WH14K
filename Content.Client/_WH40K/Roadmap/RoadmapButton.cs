using Content.Client.Localization;
using Content.Client.Stylesheets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Roadmap;

public sealed class RoadmapButton : Button, ILocalizedControl
{
    [Dependency] private readonly RoadmapManager _roadmapManager = default!;

    public RoadmapButton()
    {
        IoCManager.InjectDependencies(this);

        // Guarantees proper measurement before the manager state is applied.
        Text = " ";
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        _roadmapManager.NewRoadmapEntriesChanged += UpdateState;
        UpdateState();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _roadmapManager.NewRoadmapEntriesChanged -= UpdateState;
    }

    private void UpdateState()
    {
        if (_roadmapManager.NewRoadmapEntries)
        {
            Text = Loc.GetString("roadmap-button-new-entries");
            StyleClasses.Add(StyleClass.Negative);
        }
        else
        {
            Text = Loc.GetString("server-info-roadmap-button");
            StyleClasses.Remove(StyleClass.Negative);
        }
    }

    public void Relocalize()
    {
        UpdateState();
    }
}
