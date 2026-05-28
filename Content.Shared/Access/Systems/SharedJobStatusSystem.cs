using Content.Shared.Access.Components;
using Content.Shared.Hands;
using Content.Shared.Inventory.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.PDA;
using Content.Shared.Roles.Jobs;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.Access.Systems;

public abstract partial class SharedJobStatusSystem : EntitySystem
{
    [Dependency] private  AccessReaderSystem _accessReader = default!;
    [Dependency] private  SharedJobSystem _jobSystem = default!;
    [Dependency] private  SharedMindSystem _mindSystem = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;

    private static readonly ProtoId<JobIconPrototype> JobIconForNoId = "JobIconNoId";
    private static readonly ProtoId<JobIconPrototype> JobIconForUnknown = "JobIconUnknown";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JobStatusComponent, ComponentStartup>((uid, comp, _) => UpdateStatus((uid, comp)));
        SubscribeLocalEvent<JobStatusComponent, MindAddedMessage>((uid, comp, _) => UpdateStatus((uid, comp)));
        SubscribeLocalEvent<JobStatusComponent, MindRemovedMessage>((uid, comp, _) => UpdateStatus((uid, comp)));

        // if the mob picks up, drops or (un)equips a pda or Id card then update their crew status
        SubscribeLocalEvent<JobStatusComponent, DidEquipEvent>((uid, comp, _) => UpdateStatus((uid, comp)));
        SubscribeLocalEvent<JobStatusComponent, DidEquipHandEvent>((uid, comp, _) => UpdateStatus((uid, comp)));
        SubscribeLocalEvent<JobStatusComponent, DidUnequipEvent>((uid, comp, _) => UpdateStatus((uid, comp)));
        SubscribeLocalEvent<JobStatusComponent, DidUnequipHandEvent>((uid, comp, _) => UpdateStatus((uid, comp)));
    }

    /// <summary>
    /// Updates this mob's job and crew status depending on their currently equipped or held pda or Id card.
    /// </summary>
    public void UpdateStatus(Entity<JobStatusComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ProtoId<JobIconPrototype> iconId = JobIconForNoId;

        if (_accessReader.FindAccessItemsInventory(ent.Owner, out var items))
        {
            foreach (var item in items)
            {
                // ID Card
                if (TryComp<IdCardComponent>(item, out var id))
                {
                    iconId = id.JobIcon;
                    break;
                }

                // PDA
                if (TryComp<PdaComponent>(item, out var pda)
                    && pda.ContainedId != null
                    && TryComp(pda.ContainedId, out id))
                {
                    iconId = id.JobIcon;
                    break;
                }
            }
        }

        // If no specific icon is provided by access items, derive it from the current mind job.
        if (IsNonSpecificIcon(iconId) &&
            _mindSystem.TryGetMind(ent.Owner, out var mindId, out _) &&
            _jobSystem.MindTryGetJob(mindId, out var job))
        {
            iconId = job.Icon;
        }

        if (!_prototype.TryIndex(iconId, out var iconProto))
        {
            iconId = JobIconForNoId;
            iconProto = _prototype.Index(iconId);
        }

        ent.Comp.JobStatusIcon = iconId;
        ent.Comp.IsCrew = iconProto.IsCrewJob;
        Dirty(ent);
    }

    private static bool IsNonSpecificIcon(ProtoId<JobIconPrototype> iconId)
    {
        return iconId == JobIconForNoId || iconId == JobIconForUnknown;
    }
}
