using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Stores the player's WH40K HUD theme preference.
    ///     Auto follows faction theme assignment; any concrete theme stays fixed.
    /// </summary>
    public static readonly CVarDef<string> WH40KInterfaceThemePreference =
        CVarDef.Create("wh40k.interface_theme_preference", "WH40KAutoTheme", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Enables client-side fallback to alternate game-server addresses from the launcher connection screen.
    /// </summary>
    public static readonly CVarDef<bool> WH40KConnectionFallbackEnabled =
        CVarDef.Create("wh40k.connection_fallback.enabled", true, CVar.CLIENTONLY);

    /// <summary>
    ///     Comma-separated primary addresses that may use WH40K connection fallback. Empty allows any address.
    /// </summary>
    public static readonly CVarDef<string> WH40KConnectionFallbackPrimaryAddresses =
        CVarDef.Create("wh40k.connection_fallback.primary_addresses", "ss14://ebengrad.node-oheir.simplestation.org:25910", CVar.CLIENTONLY);

    /// <summary>
    ///     Comma-separated alternate addresses tried when the primary address fails at the transport layer.
    /// </summary>
    public static readonly CVarDef<string> WH40KConnectionFallbackAlternateAddresses =
        CVarDef.Create("wh40k.connection_fallback.alternate_addresses", "ss14://heretec.online:25910", CVar.CLIENTONLY);

    /// <summary>
    ///     Automatically switches to the first alternate address after a network-level connection failure.
    /// </summary>
    public static readonly CVarDef<bool> WH40KConnectionFallbackAutomatic =
        CVarDef.Create("wh40k.connection_fallback.automatic", true, CVar.CLIENTONLY);

    /// <summary>
    ///     Shows a manual alternate-address button on network-level connection failures.
    /// </summary>
    public static readonly CVarDef<bool> WH40KConnectionFallbackButtonEnabled =
        CVarDef.Create("wh40k.connection_fallback.button_enabled", true, CVar.CLIENTONLY);

    /// <summary>
    ///     Delay before automatic fallback reconnect starts. A small delay lets the connection state fully settle.
    /// </summary>
    public static readonly CVarDef<float> WH40KConnectionFallbackAutoDelaySeconds =
        CVarDef.Create("wh40k.connection_fallback.auto_delay_seconds", 1.0f, CVar.CLIENTONLY);

    /// <summary>
    ///     Extra delay after an established connection times out before trying an alternate address.
    ///     This gives the server time to release the old authenticated session before the same account reconnects.
    /// </summary>
    public static readonly CVarDef<float> WH40KConnectionFallbackDisconnectDelaySeconds =
        CVarDef.Create("wh40k.connection_fallback.disconnect_delay_seconds", 30.0f, CVar.CLIENTONLY);

    /// <summary>
    ///     Enables translated chat for this client when the server-side WH40K translation pipeline is active.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationPreferenceEnabled =
        CVarDef.Create("wh40k.chat_translation.preference.enabled", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Preferred incoming chat translation language for this client. Empty follows the game language.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationPreferenceLanguage =
        CVarDef.Create("wh40k.chat_translation.preference.language", string.Empty, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Enables the WH40K automatic RU/EN chat translation pipeline.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationEnabled =
        CVarDef.Create("wh40k.chat_translation.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Translation backend provider. Supported values: service, deepl.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationProvider =
        CVarDef.Create("wh40k.chat_translation.provider", "service", CVar.SERVERONLY);

    /// <summary>
    ///     Base URL of the external WH40K translation service when provider=service.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationServiceUrl =
        CVarDef.Create("wh40k.chat_translation.service_url", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional API key forwarded as X-Api-Key to the translation service when provider=service.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationApiKey =
        CVarDef.Create("wh40k.chat_translation.api_key", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     DeepL authentication key used when provider=deepl.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLAuthKey =
        CVarDef.Create("wh40k.chat_translation.deepl.auth_key", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional DeepL API base URL override. When empty, `:fx` keys use api-free.deepl.com and others use api.deepl.com.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLBaseUrl =
        CVarDef.Create("wh40k.chat_translation.deepl.base_url", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     DeepL model preference. Supported values: latency_optimized, quality_optimized, prefer_quality_optimized.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLModelType =
        CVarDef.Create("wh40k.chat_translation.deepl.model_type", "latency_optimized", CVar.SERVERONLY);

    /// <summary>
    ///     If true, asks DeepL to preserve the source formatting as much as possible.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationDeepLPreserveFormatting =
        CVarDef.Create("wh40k.chat_translation.deepl.preserve_formatting", true, CVar.SERVERONLY);

    /// <summary>
    ///     DeepL sentence splitting mode. Supported values: 0, 1, nonewlines.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLSplitSentences =
        CVarDef.Create("wh40k.chat_translation.deepl.split_sentences", "0", CVar.SERVERONLY);

    /// <summary>
    ///     Optional unbilled context string forwarded to DeepL to improve short chat translations.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLContext =
        CVarDef.Create("wh40k.chat_translation.deepl.context", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional DeepL glossary id for RU -> EN requests.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLGlossaryRuToEn =
        CVarDef.Create("wh40k.chat_translation.deepl.glossary_id.ru_en", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional DeepL glossary id for EN -> RU requests.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLGlossaryEnToRu =
        CVarDef.Create("wh40k.chat_translation.deepl.glossary_id.en_ru", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum time spent waiting for a translation before falling back to original text.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationTimeoutMs =
        CVarDef.Create("wh40k.chat_translation.timeout_ms", 1000, CVar.SERVERONLY);

    /// <summary>
    ///     Soft wait window before the original message is sent and translation continues in the background.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationSoftHoldMs =
        CVarDef.Create("wh40k.chat_translation.soft_hold_ms", 100, CVar.SERVERONLY);

    /// <summary>
    ///     Backoff window after a translation failure to avoid stalling every chat line.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationFailureBackoffSeconds =
        CVarDef.Create("wh40k.chat_translation.failure_backoff_seconds", 5, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum message length eligible for translation.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationMaxMessageLength =
        CVarDef.Create("wh40k.chat_translation.max_message_length", 256, CVar.SERVERONLY);

    /// <summary>
    ///     Local translation cache entry lifetime in seconds.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationCacheTtlSeconds =
        CVarDef.Create("wh40k.chat_translation.cache_ttl_seconds", 1800, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum amount of cached translation entries stored locally on the game server.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationCacheMaxEntries =
        CVarDef.Create("wh40k.chat_translation.cache_max_entries", 4096, CVar.SERVERONLY);

    /// <summary>
    ///     Enables translation for local IC speech.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationLocalEnabled =
        CVarDef.Create("wh40k.chat_translation.channel.local", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables translation for whisper chat.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationWhisperEnabled =
        CVarDef.Create("wh40k.chat_translation.channel.whisper", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables translation for radio chat.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationRadioEnabled =
        CVarDef.Create("wh40k.chat_translation.channel.radio", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables translation for LOOC.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationLoocEnabled =
        CVarDef.Create("wh40k.chat_translation.channel.looc", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables translation for dead chat.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationDeadEnabled =
        CVarDef.Create("wh40k.chat_translation.channel.dead", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables translation for OOC.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationOocEnabled =
        CVarDef.Create("wh40k.chat_translation.channel.ooc", true, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between WH40K team rule victory checks.
    /// </summary>
    public static readonly CVarDef<float> WH40KTeamCheckInterval =
        CVarDef.Create("wh40k.team_check_interval", 3f, CVar.SERVERONLY);

    /// <summary>
    ///     If true, victory checks are skipped until every team has at least one assigned member.
    /// </summary>
    public static readonly CVarDef<bool> WH40KRequireAllTeamsPresent =
        CVarDef.Create("wh40k.require_all_teams_present", true, CVar.SERVERONLY);

    /// <summary>
    ///     Optional global round time limit override in seconds.
    ///     Values less than or equal to 0 keep the rule prototype's own limit.
    /// </summary>
    public static readonly CVarDef<float> WH40KRoundTimeLimitSeconds =
        CVarDef.Create("wh40k.round_time_limit_seconds", 0f, CVar.SERVERONLY);

    /// <summary>
    ///     Automatically starts WH40K lobby votes after the round returns to a clean pre-round lobby state.
    /// </summary>
    public static readonly CVarDef<bool> WH40KLobbyAutoVoteEnabled =
        CVarDef.Create("wh40k.lobby_auto_vote.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Automatically starts a preset vote when WH40K lobby auto-vote is active.
    /// </summary>
    public static readonly CVarDef<bool> WH40KLobbyAutoVotePresetEnabled =
        CVarDef.Create("wh40k.lobby_auto_vote.preset_enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Automatically starts a map vote after preset voting when WH40K lobby auto-vote is active.
    /// </summary>
    public static readonly CVarDef<bool> WH40KLobbyAutoVoteMapEnabled =
        CVarDef.Create("wh40k.lobby_auto_vote.map_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Delay in seconds before the automatic WH40K lobby vote sequence begins.
    /// </summary>
    public static readonly CVarDef<float> WH40KLobbyAutoVoteDelaySeconds =
        CVarDef.Create("wh40k.lobby_auto_vote.delay_seconds", 2f, CVar.SERVERONLY);

    /// <summary>
    ///     If true, extends the lobby countdown when needed so automatic WH40K lobby votes can finish before preload.
    /// </summary>
    public static readonly CVarDef<bool> WH40KLobbyAutoVoteEnsureLobbyTime =
        CVarDef.Create("wh40k.lobby_auto_vote.ensure_lobby_time", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables or disables friendly-fire ahelp warnings for WH40K team battle.
    /// </summary>
    public static readonly CVarDef<bool> WH40KFriendlyFireAhelpEnabled =
        CVarDef.Create("wh40k.friendly_fire_ahelp_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     If true, friendly fire is blocked between teammates unless attacker has WH40KFriendlyFireAllowed.
    /// </summary>
    public static readonly CVarDef<bool> WH40KFriendlyFireDisabled =
        CVarDef.Create("wh40k.friendly_fire_disabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Cooldown in seconds between friendly-fire ahelp warnings per player.
    /// </summary>
    public static readonly CVarDef<float> WH40KFriendlyFireAhelpCooldownSeconds =
        CVarDef.Create("wh40k.friendly_fire_ahelp_cooldown_seconds", 300f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum damage required to send a friendly-fire ahelp warning. 0 disables the threshold.
    /// </summary>
    public static readonly CVarDef<float> WH40KFriendlyFireAhelpMinDamage =
        CVarDef.Create("wh40k.friendly_fire_ahelp_min_damage", 5f, CVar.SERVERONLY);

    /// <summary>
    ///     Enables the WH40K global warp instability runtime.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit)]
    public static readonly CVarDef<bool> WH40KWarpEnabled =
        CVarDef.Create("wh40k.warp.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum size of the shared global warp instability pool.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 1f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpMaxInstability =
        CVarDef.Create("wh40k.warp.max_instability", 1000f, CVar.SERVERONLY);

    /// <summary>
    ///     Passive shared warp instability recovery per second.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 1000f)]
    public static readonly CVarDef<float> WH40KWarpDecayPerSecond =
        CVarDef.Create("wh40k.warp.decay_per_second", 1.2f, CVar.SERVERONLY);

    /// <summary>
    ///     Enables personal warp backlash processing for contributors.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit)]
    public static readonly CVarDef<bool> WH40KWarpPersonalBacklashEnabled =
        CVarDef.Create("wh40k.warp.personal_backlash_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables scheduled global warp pulse announcements and effects.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit)]
    public static readonly CVarDef<bool> WH40KWarpGlobalPulsesEnabled =
        CVarDef.Create("wh40k.warp.global_pulses_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables the catastrophic max-instability outcome.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit)]
    public static readonly CVarDef<bool> WH40KWarpCatastropheEnabled =
        CVarDef.Create("wh40k.warp.catastrophe_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Probability that personal backlash chooses the highest unlocked tier instead of a lower unlocked tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 1f)]
    public static readonly CVarDef<float> WH40KWarpHighestTierChance =
        CVarDef.Create("wh40k.warp.highest_tier_chance", 0.8f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum instability needed for mild warp burn backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpMildBacklashThreshold =
        CVarDef.Create("wh40k.warp.threshold.mild_burn", 350f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum instability needed for stun + drunk backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpStunBacklashThreshold =
        CVarDef.Create("wh40k.warp.threshold.stun", 400f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum instability needed for collapse backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpCollapseBacklashThreshold =
        CVarDef.Create("wh40k.warp.threshold.collapse", 500f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum instability needed for item-drop backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpDropBacklashThreshold =
        CVarDef.Create("wh40k.warp.threshold.drop", 550f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum instability needed for bleed backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpBleedBacklashThreshold =
        CVarDef.Create("wh40k.warp.threshold.bleed", 600f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum instability needed for doppelganger backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpDoppelgangerBacklashThreshold =
        CVarDef.Create("wh40k.warp.threshold.doppelganger", 650f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum instability needed for flesh-rift backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpFleshRiftBacklashThreshold =
        CVarDef.Create("wh40k.warp.threshold.flesh_rift", 700f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum instability needed for possession backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPossessionBacklashThreshold =
        CVarDef.Create("wh40k.warp.threshold.possession", 800f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum instability needed for irreversible mutation backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpMutationBacklashThreshold =
        CVarDef.Create("wh40k.warp.threshold.mutation", 900f, CVar.SERVERONLY);

    /// <summary>
    ///     Instability threshold for the first global warp pulse tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse500Threshold =
        CVarDef.Create("wh40k.warp.pulse.500.threshold", 500f, CVar.SERVERONLY);

    /// <summary>
    ///     Instability threshold for the second global warp pulse tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse550Threshold =
        CVarDef.Create("wh40k.warp.pulse.550.threshold", 550f, CVar.SERVERONLY);

    /// <summary>
    ///     Instability threshold for the third global warp pulse tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse600Threshold =
        CVarDef.Create("wh40k.warp.pulse.600.threshold", 600f, CVar.SERVERONLY);

    /// <summary>
    ///     Instability threshold for the fourth global warp pulse tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse650Threshold =
        CVarDef.Create("wh40k.warp.pulse.650.threshold", 650f, CVar.SERVERONLY);

    /// <summary>
    ///     Instability threshold for the fifth global warp pulse tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse700Threshold =
        CVarDef.Create("wh40k.warp.pulse.700.threshold", 700f, CVar.SERVERONLY);

    /// <summary>
    ///     Instability threshold for the sixth global warp pulse tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse750Threshold =
        CVarDef.Create("wh40k.warp.pulse.750.threshold", 750f, CVar.SERVERONLY);

    /// <summary>
    ///     Instability threshold for the seventh global warp pulse tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse800Threshold =
        CVarDef.Create("wh40k.warp.pulse.800.threshold", 800f, CVar.SERVERONLY);

    /// <summary>
    ///     Instability threshold for the eighth global warp pulse tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse850Threshold =
        CVarDef.Create("wh40k.warp.pulse.850.threshold", 850f, CVar.SERVERONLY);

    /// <summary>
    ///     Instability threshold for the ninth global warp pulse tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse900Threshold =
        CVarDef.Create("wh40k.warp.pulse.900.threshold", 900f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between 500/550-tier global pulses. 0 disables the interval group.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse500IntervalSeconds =
        CVarDef.Create("wh40k.warp.pulse.500.interval_seconds", 60f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between 600/650-tier global pulses. 0 disables the interval group.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse600IntervalSeconds =
        CVarDef.Create("wh40k.warp.pulse.600.interval_seconds", 45f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between 700/750-tier global pulses. 0 disables the interval group.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse700IntervalSeconds =
        CVarDef.Create("wh40k.warp.pulse.700.interval_seconds", 30f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between 800/850-tier global pulses. 0 disables the interval group.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse800IntervalSeconds =
        CVarDef.Create("wh40k.warp.pulse.800.interval_seconds", 20f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between 900-tier global pulses. 0 disables the tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpPulse900IntervalSeconds =
        CVarDef.Create("wh40k.warp.pulse.900.interval_seconds", 11f, CVar.SERVERONLY);

    /// <summary>
    ///     Heat damage dealt by the mild warp-burn backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpMildBurnDamage =
        CVarDef.Create("wh40k.warp.mild_burn_damage", 10f, CVar.SERVERONLY);

    /// <summary>
    ///     Stun duration in seconds for the stun backlash tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpStunDurationSeconds =
        CVarDef.Create("wh40k.warp.stun_duration_seconds", 1f, CVar.SERVERONLY);

    /// <summary>
    ///     Drunkenness duration in seconds for the stun backlash tier.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpStunDrunkennessSeconds =
        CVarDef.Create("wh40k.warp.stun_drunkenness_seconds", 10f, CVar.SERVERONLY);

    /// <summary>
    ///     Stun duration in seconds for the collapse backlash branch.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpCollapseStunSeconds =
        CVarDef.Create("wh40k.warp.collapse_stun_seconds", 5f, CVar.SERVERONLY);

    /// <summary>
    ///     Drunkenness duration in seconds for the collapse backlash branch.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpCollapseDrunkennessSeconds =
        CVarDef.Create("wh40k.warp.collapse_drunkenness_seconds", 20f, CVar.SERVERONLY);

    /// <summary>
    ///     Target bleed amount for the heavy bleed backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpBleedTarget =
        CVarDef.Create("wh40k.warp.bleed_target", 5f, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum number of items that can be forced out of hands/inventory by the drop backlash.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 1, max: 64)]
    public static readonly CVarDef<int> WH40KWarpDropMaxCount =
        CVarDef.Create("wh40k.warp.drop_max_count", 3, CVar.SERVERONLY);

    /// <summary>
    ///     Chance that flesh-rift backlash polymorphs the target into a hellspawn.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 1f)]
    public static readonly CVarDef<float> WH40KWarpFleshRiftDemonChance =
        CVarDef.Create("wh40k.warp.flesh_rift_demon_chance", 0.15f, CVar.SERVERONLY);

    /// <summary>
    ///     Chance that flesh-rift backlash kills the target after the demon roll fails.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 1f)]
    public static readonly CVarDef<float> WH40KWarpFleshRiftDeathChance =
        CVarDef.Create("wh40k.warp.flesh_rift_death_chance", 0.35f, CVar.SERVERONLY);

    /// <summary>
    ///     Heat damage dealt by the lethal flesh-rift backlash branch.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 100000f)]
    public static readonly CVarDef<float> WH40KWarpFleshRiftDeathDamage =
        CVarDef.Create("wh40k.warp.flesh_rift_death_damage", 500f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum irreversible mutation severity rolled when mutation backlash occurs.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 1f)]
    public static readonly CVarDef<float> WH40KWarpMutationMinSeverity =
        CVarDef.Create("wh40k.warp.mutation_min_severity", 0.25f, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum irreversible mutation severity rolled when mutation backlash occurs.
    /// </summary>
    [CVarControl(AdminFlags.VarEdit, min: 0f, max: 1f)]
    public static readonly CVarDef<float> WH40KWarpMutationMaxSeverity =
        CVarDef.Create("wh40k.warp.mutation_max_severity", 0.75f, CVar.SERVERONLY);

    /// <summary>
    ///     Enables the WH40K vignette fullscreen post-process shader.
    /// </summary>
    public static readonly CVarDef<bool> WH40KGrimdarkShaderEnabled =
        CVarDef.Create("wh40k.grimdark_shader_enabled", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Account-level cap for WH40K player meta progression. 0 means no cap.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaLevelCap =
        CVarDef.Create("wh40k.meta.level_cap", 40, CVar.SERVERONLY);

    /// <summary>
    ///     XP multiplier for WH40K player meta progression.
    /// </summary>
    public static readonly CVarDef<float> WH40KMetaXpMultiplier =
        CVarDef.Create("wh40k.meta.xp_multiplier", 1.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Legacy compatibility flag for meta unlock checks.
    ///     If true, level/achievement requirements are bypassed for decoration selection and meta-gated loadouts.
    /// </summary>
    public static readonly CVarDef<bool> WH40KMetaUnlocksEnforced =
        CVarDef.Create("wh40k.meta.unlocks_enforced", false, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     If true, admin visual overrides keep priority over WH40K OOC/ghost decorations.
    ///     If false, WH40K decorations are allowed to style admin chat/ghost visuals as well.
    /// </summary>
    public static readonly CVarDef<bool> WH40KMetaAdminPriorityOverDecorations =
        CVarDef.Create("wh40k.meta.admin_priority_over_decorations", true, CVar.SERVERONLY);

    /// <summary>
    ///     Controls whether WH40K decoration styling is applied to the whole OOC line (`OOC:`, name, and message).
    ///     Modes: 0 = off, 1 = admins only, 2 = all players.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaOocDecorationLineMode =
        CVarDef.Create("wh40k.meta.ooc_decoration_line_mode", 0, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for winning WH40K round.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpRoundWin =
        CVarDef.Create("wh40k.meta.xp_round_win", 200, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for valid WH40K kill.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpKill =
        CVarDef.Create("wh40k.meta.xp_kill", 15, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum amount of kill XP per player in one round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpKillCapPerRound =
        CVarDef.Create("wh40k.meta.xp_kill_cap_per_round", 150, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective major outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveMajor =
        CVarDef.Create("wh40k.meta.xp_objective_major", 50, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective minor outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveMinor =
        CVarDef.Create("wh40k.meta.xp_objective_minor", 25, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective timeout outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveTimeout =
        CVarDef.Create("wh40k.meta.xp_objective_timeout", 10, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective failure outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveFailure =
        CVarDef.Create("wh40k.meta.xp_objective_failure", 0, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum objective XP per player per round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveCapPerRound =
        CVarDef.Create("wh40k.meta.xp_objective_cap_per_round", 300, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum repeatable WH40K round XP per player per round.
    ///     Achievement reward XP is not counted towards this cap.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpRepeatableCapPerRound =
        CVarDef.Create("wh40k.meta.xp_repeatable_cap_per_round", 1000, CVar.SERVERONLY);

    /// <summary>
    ///     Enables verbose server trace logs for WH40K meta/stat progression pipeline.
    /// </summary>
    public static readonly CVarDef<bool> WH40KMetaStatsTrace =
        CVarDef.Create("wh40k.meta.stats_trace", false, CVar.SERVERONLY);

    /// <summary>
    ///     Enables verbose anti-farm validation logs for WH40K round rewards.
    /// </summary>
    public static readonly CVarDef<bool> WH40KMetaAntiFarmTrace =
        CVarDef.Create("wh40k.meta.anti_farm_trace", false, CVar.SERVERONLY);

    /// <summary>
    ///     Enables verbose in-round WH40K economy telemetry (team CP/FP deltas, spending and periodic snapshots).
    /// </summary>
    public static readonly CVarDef<bool> WH40KEconomyTelemetryTrace =
        CVarDef.Create("wh40k.economy.telemetry_trace", false, CVar.SERVERONLY);

    /// <summary>
    ///     Enables verbose WH40K mission-runtime debug logs (objective anchors, cargo routing, mission flow).
    /// </summary>
    public static readonly CVarDef<bool> WH40KMissionRuntimeDebugTrace =
        CVarDef.Create("wh40k.mission_runtime.debug_trace", false, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between periodic WH40K economy telemetry snapshots.
    /// </summary>
    public static readonly CVarDef<float> WH40KEconomyTelemetrySnapshotIntervalSeconds =
        CVarDef.Create("wh40k.economy.telemetry_snapshot_interval_seconds", 180f, CVar.SERVERONLY);

    /// <summary>
    ///     Absolute CP delta threshold that marks a telemetry line as a burst.
    /// </summary>
    public static readonly CVarDef<int> WH40KEconomyTelemetryBurstCommandDelta =
        CVarDef.Create("wh40k.economy.telemetry_burst_cp_delta", 24, CVar.SERVERONLY);

    /// <summary>
    ///     Enables WH40K projectile prediction reconciliation (client hit report + server validation).
    /// </summary>
    public static readonly CVarDef<bool> WH40KGunPrediction =
        CVarDef.Create("wh40k.gun_prediction", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum allowed deviation for client-reported coordinates around lag-compensated target position.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionCoordinateDeviation =
        CVarDef.Create("wh40k.gun_prediction_coordinate_deviation", 1.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Secondary wider deviation window for older lag-comp snapshot fallback.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionLowestCoordinateDeviation =
        CVarDef.Create("wh40k.gun_prediction_lowest_coordinate_deviation", 1.5f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Expands server-side fixture bounds during prediction validation to reduce false negatives.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionAabbEnlargement =
        CVarDef.Create("wh40k.gun_prediction_aabb_enlargement", 0.1f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum age (seconds) for a predicted hit report since projectile spawn. 0 disables the age gate.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionMaxReportAgeSeconds =
        CVarDef.Create("wh40k.gun_prediction_max_report_age_seconds", 2.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum number of targets accepted in one predicted hit report payload.
    /// </summary>
    public static readonly CVarDef<int> WH40KGunPredictionMaxHitsPerReport =
        CVarDef.Create("wh40k.gun_prediction_max_hits_per_report", 8, CVar.SERVERONLY);

    /// <summary>
    ///     Enables debug logs for rejected WH40K predicted projectile hit reports.
    /// </summary>
    public static readonly CVarDef<bool> WH40KGunPredictionLogRejectedHits =
        CVarDef.Create("wh40k.gun_prediction_log_rejected_hits", false, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum front-point reward budget per team from tactical fulton extraction in one round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KFultonFrontRewardCapPerRound =
        CVarDef.Create("wh40k.fulton_front_reward_cap_per_round", 80, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum command-point reward budget per team from tactical fulton extraction in one round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KFultonCommandRewardCapPerRound =
        CVarDef.Create("wh40k.fulton_command_reward_cap_per_round", 80, CVar.SERVERONLY);

    /// <summary>
    ///     Enables mission-runtime cargo completion hook from successful tactical fulton extraction.
    /// </summary>
    public static readonly CVarDef<bool> WH40KFultonMissionHookEnabled =
        CVarDef.Create("wh40k.fulton_mission_hook_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables the WH40K Discord authorization / account-linking feature set.
    /// </summary>
    public static readonly CVarDef<bool> WH40KDiscordAuthEnabled =
        CVarDef.Create("wh40k.discord_auth_enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Discord OAuth2 application client id used for account linking.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthClientId =
        CVarDef.Create("wh40k.discord_auth_client_id", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Discord OAuth2 application client secret used for token exchange.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthClientSecret =
        CVarDef.Create("wh40k.discord_auth_client_secret", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Exact redirect URI configured in the Discord application for the callback endpoint.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthRedirectUri =
        CVarDef.Create("wh40k.discord_auth_redirect_uri", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Discord guild id used to validate server membership and role access.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthGuildId =
        CVarDef.Create("wh40k.discord_auth_guild_id", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     If true, players must link a Discord account to satisfy WH40K Discord access policy.
    /// </summary>
    public static readonly CVarDef<bool> WH40KDiscordAuthRequireLink =
        CVarDef.Create("wh40k.discord_auth_require_link", false, CVar.SERVERONLY);

    /// <summary>
    ///     If true, players must be a member of the configured Discord guild to satisfy WH40K Discord access policy.
    /// </summary>
    public static readonly CVarDef<bool> WH40KDiscordAuthRequireGuildMember =
        CVarDef.Create("wh40k.discord_auth_require_guild_member", false, CVar.SERVERONLY);

    /// <summary>
    ///     Comma-separated Discord role ids. If set, players must have at least one of these roles to satisfy WH40K Discord access policy.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthRequiredRoleIds =
        CVarDef.Create("wh40k.discord_auth_required_role_ids", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     If true, enforce the configured WH40K Discord access policy during server connection approval instead of lobby/round flow.
    /// </summary>
    public static readonly CVarDef<bool> WH40KDiscordAuthGateOnConnect =
        CVarDef.Create("wh40k.discord_auth_gate_on_connect", false, CVar.SERVERONLY);

    /// <summary>
    ///     Lifetime in seconds for a pending Discord link request state token.
    /// </summary>
    public static readonly CVarDef<int> WH40KDiscordAuthLinkRequestTtlSeconds =
        CVarDef.Create("wh40k.discord_auth_link_request_ttl_seconds", 600, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum seconds between manual profile refresh requests for one player.
    /// </summary>
    public static readonly CVarDef<int> WH40KDiscordAuthRefreshCooldownSeconds =
        CVarDef.Create("wh40k.discord_auth_refresh_cooldown_seconds", 30, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum seconds between automatic/manual Discord refresh attempts triggered from server connect-gate flow.
    /// </summary>
    public static readonly CVarDef<int> WH40KDiscordAuthConnectRefreshCooldownSeconds =
        CVarDef.Create("wh40k.discord_auth_connect_refresh_cooldown_seconds", 30, CVar.SERVERONLY);

    /// <summary>
    ///     How long cached guild membership / role data should be considered fresh for display purposes.
    /// </summary>
    public static readonly CVarDef<int> WH40KDiscordAuthCacheTtlMinutes =
        CVarDef.Create("wh40k.discord_auth_cache_ttl_minutes", 720, CVar.SERVERONLY);

    /// <summary>
    ///     Shared secret for authenticating relay callback requests from an external auth proxy.
    ///     When set, the game server accepts POST /wh40k/discord-auth/relay with code+state
    ///     forwarded by the external server. Must match the secret configured on the proxy.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthRelaySecret =
        CVarDef.Create("wh40k.discord_auth_relay_secret", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Enables periodic WH40K net-buffer diagnostics logs (traffic bursts, blocked channels, dirty hot spots).
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagEnabled =
        CVarDef.Create("wh40k.netdiag.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Enables WH40K-local source attribution markers (who dirtied what) for net diagnostics.
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagAttributionEnabled =
        CVarDef.Create("wh40k.netdiag.attribution_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Comma-separated WH40K source scopes for attribution.
    ///     Empty value or "*" enables all scopes.
    /// </summary>
    public static readonly CVarDef<string> WH40KNetDiagAttributionScopes =
        CVarDef.Create("wh40k.netdiag.attribution_scopes", "*", CVar.SERVERONLY);

    /// <summary>
    ///     If true, auto-captures Dirty callsites for all WH40K systems via stack attribution.
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagAttributionAutoDirtyEnabled =
        CVarDef.Create("wh40k.netdiag.attribution_auto_dirty_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum stack depth inspected for automatic WH40K Dirty callsite attribution.
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagAttributionAutoDirtyStackDepth =
        CVarDef.Create("wh40k.netdiag.attribution_auto_dirty_stack_depth", 24, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between WH40K net diagnostics snapshots.
    /// </summary>
    public static readonly CVarDef<float> WH40KNetDiagSampleIntervalSeconds =
        CVarDef.Create("wh40k.netdiag.sample_interval_seconds", 1.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Outgoing throughput threshold (KiB/s) that marks a snapshot as a burst. 0 disables this trigger.
    /// </summary>
    public static readonly CVarDef<float> WH40KNetDiagBurstOutgoingKiBPerSecond =
        CVarDef.Create("wh40k.netdiag.burst_outgoing_kib_per_sec", 512f, CVar.SERVERONLY);

    /// <summary>
    ///     Outgoing packet-rate threshold (packets/s) that marks a snapshot as a burst. 0 disables this trigger.
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagBurstOutgoingPacketsPerSecond =
        CVarDef.Create("wh40k.netdiag.burst_outgoing_packets_per_sec", 1200, CVar.SERVERONLY);

    /// <summary>
    ///     If true, emits a diagnostics line every sampling window, not only on burst/blocked-channel cases.
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagTraceEverySample =
        CVarDef.Create("wh40k.netdiag.trace_every_sample", false, CVar.SERVERONLY);

    /// <summary>
    ///     Number of top entries to print in diagnostics sections (message types, entities, prototypes, clients).
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagTopEntries =
        CVarDef.Create("wh40k.netdiag.top_entries", 8, CVar.SERVERONLY);

    /// <summary>
    ///     Ping (ms) above which a channel is considered high-latency in diagnostics.
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagHighPingMs =
        CVarDef.Create("wh40k.netdiag.high_ping_ms", 220, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum number of blocked channel details printed in one diagnostics line.
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagMaxBlockedClientDetails =
        CVarDef.Create("wh40k.netdiag.max_blocked_client_details", 6, CVar.SERVERONLY);

    /// <summary>
    ///     Warns if per-message bandwidth buckets are unavailable (for example non-DEBUG networking builds).
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagWarnNoTypeMetrics =
        CVarDef.Create("wh40k.netdiag.warn_no_type_metrics", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables WH40K database diagnostics (latency/error aggregation for meta/disc auth pipelines).
    /// </summary>
    public static readonly CVarDef<bool> WH40KDbDiagEnabled =
        CVarDef.Create("wh40k.dbdiag.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between WH40K DB diagnostics summaries.
    /// </summary>
    public static readonly CVarDef<float> WH40KDbDiagSampleIntervalSeconds =
        CVarDef.Create("wh40k.dbdiag.sample_interval_seconds", 10.0f, CVar.SERVERONLY);

    /// <summary>
    ///     DB operation duration (ms) considered slow.
    /// </summary>
    public static readonly CVarDef<int> WH40KDbDiagSlowMs =
        CVarDef.Create("wh40k.dbdiag.slow_ms", 150, CVar.SERVERONLY);

    /// <summary>
    ///     DB operation duration (ms) considered critical.
    /// </summary>
    public static readonly CVarDef<int> WH40KDbDiagCriticalMs =
        CVarDef.Create("wh40k.dbdiag.critical_ms", 1000, CVar.SERVERONLY);

    /// <summary>
    ///     If true, emits WH40K DB diagnostics each sampling window even without anomalies.
    /// </summary>
    public static readonly CVarDef<bool> WH40KDbDiagTraceEverySample =
        CVarDef.Create("wh40k.dbdiag.trace_every_sample", false, CVar.SERVERONLY);

    /// <summary>
    ///     Number of top DB operations included in WH40K DB diagnostics output.
    /// </summary>
    public static readonly CVarDef<int> WH40KDbDiagTopEntries =
        CVarDef.Create("wh40k.dbdiag.top_entries", 8, CVar.SERVERONLY);

    /// <summary>
    ///     Enables ambient outdoor atmosphere recovery for unroofed tiles on non-space maps.
    /// </summary>
    public static readonly CVarDef<bool> WH40KOutdoorAtmosphereEnabled =
        CVarDef.Create("wh40k.outdoor_atmosphere.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between outdoor atmosphere recovery passes.
    /// </summary>
    public static readonly CVarDef<float> WH40KOutdoorAtmosphereIntervalSeconds =
        CVarDef.Create("wh40k.outdoor_atmosphere.interval_seconds", 2.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Fraction of gas difference corrected per outdoor recovery pass.
    /// </summary>
    public static readonly CVarDef<float> WH40KOutdoorAtmosphereBlendFactor =
        CVarDef.Create("wh40k.outdoor_atmosphere.blend_factor", 0.5f, CVar.SERVERONLY);

    /// <summary>
    ///     Fraction of temperature difference corrected per outdoor recovery pass.
    /// </summary>
    public static readonly CVarDef<float> WH40KOutdoorAtmosphereTemperatureBlendFactor =
        CVarDef.Create("wh40k.outdoor_atmosphere.temperature_blend_factor", 0.25f, CVar.SERVERONLY);

}
