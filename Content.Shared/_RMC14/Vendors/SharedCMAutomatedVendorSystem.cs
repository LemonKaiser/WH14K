using System;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.UserInterface;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._RMC14.Vendors;

public abstract partial class SharedCMAutomatedVendorSystem : EntitySystem
{
    [Dependency] private  SharedAudioSystem _audio = default!;
    [Dependency] private  SharedHandsSystem _hands = default!;
    [Dependency] private  SharedJobSystem _job = default!;
    [Dependency] private  SharedMindSystem _mind = default!;
    [Dependency] private  INetManager _net = default!;
    [Dependency] private  IPrototypeManager _prototypes = default!;
    [Dependency] private  IRobustRandom _random = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CMAutomatedVendorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CMAutomatedVendorComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);

        Subs.BuiEvents<CMAutomatedVendorComponent>(CMAutomatedVendorUI.Key, subs =>
        {
            subs.Event<CMVendorVendBuiMsg>(OnVendBui);
        });
    }

    private void OnMapInit(Entity<CMAutomatedVendorComponent> ent, ref MapInitEvent args)
    {
        foreach (var section in ent.Comp.Sections)
        {
            foreach (var entry in section.Entries)
            {
                if (entry.Amount is < 0)
                    entry.Amount = 0;
            }
        }

        Dirty(ent);
    }

    private void OnOpenAttempt(Entity<CMAutomatedVendorComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || HasComp<BypassInteractionChecksComponent>(args.User))
            return;

        if (HasAnyAllowedJob(args.User, ent.Comp.Jobs))
            return;

        args.Cancel();
    }

    protected virtual void OnVendBui(Entity<CMAutomatedVendorComponent> vendor, ref CMVendorVendBuiMsg args)
    {
        if (vendor.Comp.Sound != null)
            _audio.PlayPredicted(vendor.Comp.Sound, vendor, args.Actor);

        if (_net.IsClient)
            return;

        if ((uint) args.Section >= (uint) vendor.Comp.Sections.Count)
            return;

        var section = vendor.Comp.Sections[args.Section];

        if ((uint) args.Entry >= (uint) section.Entries.Count)
            return;

        if (!HasAnyAllowedJob(args.Actor, section.Jobs))
            return;

        var entry = section.Entries[args.Entry];
        if (entry.Amount is <= 0)
            return;

        if (!_prototypes.HasIndex(entry.Id))
            return;

        var user = ResolveUserComponent(args.Actor, section, entry);

        if (section.Choices is { } choices)
        {
            var current = user!.Choices.GetValueOrDefault(choices.Id);
            if (current >= choices.Amount)
                return;

            user.Choices[choices.Id] = current + 1;
        }

        if (section.TakeAll is { Length: > 0 } takeAll)
        {
            var key = $"{takeAll}:{entry.Id}";
            if (!user!.TakeAll.TryAdd(key, true))
                return;
        }

        if (section.TakeOne is { Length: > 0 } takeOne)
        {
            if (!user!.TakeOne.TryAdd(takeOne, true))
                return;
        }

        if (entry.Points is { } points)
        {
            if (user!.Points < points)
                return;

            user.Points -= points;
        }

        if (entry.Amount != null)
            entry.Amount = Math.Max(0, entry.Amount.Value - 1);

        if (user != null)
            Dirty(args.Actor, user);

        Dirty(vendor);

        var spawnCount = Math.Max(1, entry.Spawn);
        for (var i = 0; i < spawnCount; i++)
        {
            var spawned = SpawnNextToOrDrop(entry.Id, vendor);
            if (_hands.TryPickupAnyHand(args.Actor, spawned))
                continue;

            if (!TryComp(spawned, out TransformComponent? xform))
                continue;

            var offset = _random.NextVector2Box(
                vendor.Comp.MinOffset.X,
                vendor.Comp.MinOffset.Y,
                vendor.Comp.MaxOffset.X,
                vendor.Comp.MaxOffset.Y);
            _transform.SetLocalPosition(spawned, xform.LocalPosition + offset, xform);
        }
    }

    private bool HasAnyAllowedJob(EntityUid user, List<ProtoId<JobPrototype>> jobs)
    {
        if (jobs.Count == 0)
            return true;

        if (!_mind.TryGetMind(user, out var mindId, out _))
            return false;

        foreach (var job in jobs)
        {
            if (_job.MindHasJobWithId(mindId, job.Id))
                return true;
        }

        return false;
    }

    private CMVendorUserComponent? ResolveUserComponent(EntityUid actor, CMVendorSection section, CMVendorEntry entry)
    {
        var requiresUserData = section.Choices != null ||
                               section.TakeAll != null ||
                               section.TakeOne != null ||
                               entry.Points != null;
        if (!requiresUserData)
            return null;

        return EnsureComp<CMVendorUserComponent>(actor);
    }
}
