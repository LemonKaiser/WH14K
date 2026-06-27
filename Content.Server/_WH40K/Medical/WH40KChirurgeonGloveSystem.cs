using Content.Shared._WH40K.Medical;
using Content.Shared.Body;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Traits.Assorted;
using Content.Shared.Verbs;

namespace Content.Server._WH40K.Medical;

public sealed partial class WH40KChirurgeonGloveSystem : EntitySystem
{
    private const string ExtractVerbLoc = "wh40k-chirurgeon-glove-verb";
    private const string InvalidLoc = "wh40k-chirurgeon-glove-popup-invalid";
    private const string StartLoc = "wh40k-chirurgeon-glove-popup-start";
    private const string SuccessLoc = "wh40k-chirurgeon-glove-popup-success";

    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, GetVerbsEvent<InteractionVerb>>(OnGetInteractionVerbs);
        SubscribeLocalEvent<BodyComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WH40KChirurgeonGloveComponent, DoAfterAttemptEvent<WH40KChirurgeonSkullExtractionDoAfterEvent>>(OnExtractionAttempt);
        SubscribeLocalEvent<WH40KChirurgeonGloveComponent, WH40KChirurgeonSkullExtractionDoAfterEvent>(OnExtractionDoAfter);
    }

    private void OnGetInteractionVerbs(Entity<BodyComponent> target, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryGetEquippedExtractor(args.User, out var extractor))
            return;

        if (!CanStartExtraction(extractor.Owner, args.User, target.Owner, extractor.Comp, popup: false))
            return;

        var extractorUid = extractor.Owner;
        var user = args.User;
        var targetUid = target.Owner;
        var glove = extractor.Comp;

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString(ExtractVerbLoc),
            Act = () => StartExtraction(extractorUid, user, targetUid, glove),
            Priority = 25,
            Impact = LogImpact.Medium,
        });
    }

    private void OnInteractUsing(Entity<BodyComponent> target, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<WH40KChirurgeonGloveComponent>(args.Used, out var extractor))
            return;

        if (!CanStartExtraction(args.Used, args.User, target.Owner, extractor, popup: true))
            return;

        StartExtraction(args.Used, args.User, target.Owner, extractor);
        args.Handled = true;
    }

    private void OnExtractionAttempt(Entity<WH40KChirurgeonGloveComponent> extractor, ref DoAfterAttemptEvent<WH40KChirurgeonSkullExtractionDoAfterEvent> args)
    {
        if (args.DoAfter.Args.Target is not { } target ||
            !CanStartExtraction(extractor.Owner, args.DoAfter.Args.User, target, extractor.Comp, popup: false))
        {
            args.Cancel();
        }
    }

    private void OnExtractionDoAfter(Entity<WH40KChirurgeonGloveComponent> extractor, ref WH40KChirurgeonSkullExtractionDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        if (!CanStartExtraction(extractor.Owner, args.User, target, extractor.Comp, popup: false))
            return;

        if (!TryFindHead(target, out var head))
            return;

        args.Handled = true;

        EnsureComp<UnrevivableComponent>(target);
        _mobThreshold.SetAllowRevives(target, false);

        _transform.AttachToGridOrMap(head);
        QueueDel(head);

        var skull = Spawn(extractor.Comp.SkullPrototype, Transform(target).Coordinates);
        _hands.TryPickupAnyHand(args.User, skull, checkActionBlocker: false);
        _popup.PopupEntity(Loc.GetString(SuccessLoc), skull, args.User, PopupType.Medium);
    }

    private void StartExtraction(EntityUid extractor, EntityUid user, EntityUid target, WH40KChirurgeonGloveComponent comp)
    {
        if (!CanStartExtraction(extractor, user, target, comp, popup: true))
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager, user, comp.DoAfter, new WH40KChirurgeonSkullExtractionDoAfterEvent(), extractor, target: target, used: extractor)
        {
            NeedHand = _hands.IsHolding(user, extractor),
            BreakOnDamage = true,
            BreakOnMove = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
            DistanceThreshold = 1.5f,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
            _popup.PopupEntity(Loc.GetString(StartLoc), target, user, PopupType.Small);
    }

    private bool CanStartExtraction(
        EntityUid extractor,
        EntityUid user,
        EntityUid target,
        WH40KChirurgeonGloveComponent? glove = null,
        MobStateComponent? mobState = null,
        bool popup = false)
    {
        var valid =
            Resolve(extractor, ref glove, false) &&
            Resolve(target, ref mobState, false) &&
            IsExtractorAvailableToUser(extractor, user) &&
            _mobState.IsDead(target, mobState) &&
            TryFindHead(target, out _);

        if (!valid && popup)
            _popup.PopupEntity(Loc.GetString(InvalidLoc), target, user, PopupType.SmallCaution);

        return valid;
    }

    private bool IsExtractorAvailableToUser(EntityUid extractor, EntityUid user)
    {
        if (_inventory.TryGetSlotEntity(user, "gloves", out var equipped) && equipped == extractor)
            return true;

        return _hands.IsHolding(user, extractor);
    }

    private bool TryGetEquippedExtractor(EntityUid user, out Entity<WH40KChirurgeonGloveComponent> extractor)
    {
        extractor = default;

        if (!_inventory.TryGetSlotEntity(user, "gloves", out var gloves) ||
            !TryComp(gloves.Value, out WH40KChirurgeonGloveComponent? comp))
        {
            return false;
        }

        extractor = (gloves.Value, comp);
        return true;
    }

    private bool TryFindHead(EntityUid target, out EntityUid head)
    {
        head = default;

        if (!TryComp<BodyComponent>(target, out var body) || body.Organs == null)
            return false;

        foreach (var organ in body.Organs.ContainedEntities)
        {
            if (TryComp<OrganComponent>(organ, out var organComp) && organComp.Category == "Head")
            {
                head = organ;
                return true;
            }
        }

        return false;
    }
}
