using System.Linq;
using Content.Server.Verbs;
using Content.Shared.Examine;
using Content.Shared.Localizations;
using Content.Shared.Verbs;
using JetBrains.Annotations;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Examine
{
    [UsedImplicitly]
    public sealed partial class ExamineSystem : ExamineSystemShared
    {
        [Dependency] private ILocalizationManager _localizationManager = default!;
        [Dependency] private VerbSystem _verbSystem = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeNetworkEvent<ExamineSystemMessages.RequestExamineInfoMessage>(ExamineInfoRequest);
        }

        public override void SendExamineTooltip(EntityUid player, EntityUid target, FormattedMessage message, bool getVerbs, bool centerAtCursor)
        {
            if (!TryComp<ActorComponent>(player, out var actor))
                return;

            var session = actor.PlayerSession;

            SortedSet<Verb>? verbs = null;
            if (getVerbs)
                verbs = _verbSystem.GetLocalVerbs(target, player, typeof(ExamineVerb));

            var ev = new ExamineSystemMessages.ExamineInfoResponseMessage(
                GetNetEntity(target), 0, message, verbs?.ToList(), centerAtCursor, cultureName: _localizationManager.GetCurrentCultureName()
            );

            RaiseNetworkEvent(ev, session.Channel);
        }

        private void ExamineInfoRequest(ExamineSystemMessages.RequestExamineInfoMessage request, EntitySessionEventArgs eventArgs)
        {
            using var cultureScope = new LocalizationCultureScope(_localizationManager, request.CultureName);

            var player = eventArgs.SenderSession;
            var session = eventArgs.SenderSession;
            var channel = player.Channel;
            var entity = GetEntity(request.NetEntity);

            if (session.AttachedEntity is not {Valid: true} playerEnt
                || !Exists(entity))
            {
                RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                    request.NetEntity, request.Id, CreateMessage("examine-system-entity-does-not-exist"), cultureName: request.CultureName), channel);
                return;
            }

            if (!CanExamine(playerEnt, entity))
            {
                RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                    request.NetEntity, request.Id, CreateMessage("examine-system-cant-see-entity"), knowTarget: false, cultureName: request.CultureName), channel);
                return;
            }

            SortedSet<Verb>? verbs = null;
            if (request.GetVerbs)
                verbs = _verbSystem.GetLocalVerbs(entity, playerEnt, typeof(ExamineVerb));

            var text = GetExamineText(entity, player.AttachedEntity);
            RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                request.NetEntity, request.Id, text, verbs?.ToList(), cultureName: request.CultureName), channel);
        }

        private FormattedMessage CreateMessage(string locId)
        {
            var message = new FormattedMessage();
            message.AddText(Loc.GetString(locId));
            return message;
        }
    }
}
