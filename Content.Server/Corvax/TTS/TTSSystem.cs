using System.Threading.Tasks;
using Content.Server.Chat.Systems;
using Content.Shared.CCVar;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Corvax.TTS;
using Content.Shared.GameTicking;
using Content.Shared.Players.RateLimiting;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server.DeadSpace.Languages;
using Robust.Server.Player;
using System.Linq;

namespace Content.Server.Corvax.TTS;

// ReSharper disable once InconsistentNaming
public sealed partial class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly TTSManager _ttsManager = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly IRobustRandom _rng = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly List<string> _sampleText =
        new()
        {
            "Съешь же ещё этих мягких французских булок, да выпей чаю.",
            "Клоун, прекрати разбрасывать банановые кожурки офицерам под ноги!",
            "Капитан, вы уверены что хотите назначить клоуна на должность главы персонала?",
            "Эс Бэ! Тут человек в сером костюме, с тулбоксом и в маске! Помогите!!",
            "Я надеюсь что инженеры внимательно следят за сингулярностью...",
            "Вы слышали эти странные крики в техах? Мне кажется туда ходить небезопасно.",
            "Вы не видели Гамлета? Мне кажется он забегал к вам на кухню.",
            "Здесь есть доктор? Человек умирает от отравленного пончика! Нужна помощь!",
            "Возле эвакуационного шаттла разгерметизация! Инженеры, нам срочно нужна ваша помощь!",
            "Бармен, налей мне самого крепкого вина, которое есть в твоих запасах!"
        };

    private const int MaxMessageChars = 100 * 3; // same as SingleBubbleCharLimit * 3
    private bool _isEnabled = false;

    public override void Initialize()
    {
        _cfg.OnValueChanged(CCCVars.TTSEnabled, v => _isEnabled = v, true);

        SubscribeLocalEvent<TransformSpeechEvent>(OnTransformSpeech);
        SubscribeLocalEvent<TTSComponent, EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<TTSComponent, EntitySpokeToEntityEvent>(OnEntitySpokeToEntity);
        SubscribeLocalEvent<RadioSpokeEvent>(OnRadioSpokeEvent);
        SubscribeLocalEvent<AnnounceSpokeEvent>(OnAnnounceSpokeEvent);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeNetworkEvent<RequestPreviewTTSEvent>(OnRequestPreviewTTS);

        RegisterRateLimits();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _ttsManager.ResetCache();
    }

    private async void OnRequestPreviewTTS(RequestPreviewTTSEvent ev, EntitySessionEventArgs args)
    {
        if (!_isEnabled ||
            !_prototypeManager.TryIndex<TTSVoicePrototype>(ev.VoiceId, out var protoVoice))
            return;

        if (HandleRateLimit(args.SenderSession) != RateLimitStatus.Allowed)
            return;

        var previewText = _rng.Pick(_sampleText);
        var soundData = await GenerateTTS(previewText, protoVoice.Speaker);
        if (soundData is null)
            return;

        RaiseNetworkEvent(new PlayTTSEvent(soundData), Filter.SinglePlayer(args.SenderSession));
    }

    private async void OnEntitySpoke(EntityUid uid, TTSComponent component, EntitySpokeEvent args)
    {
        var voiceId = component.VoicePrototypeId;
        if (!_isEnabled ||
            args.Message.Length > MaxMessageChars ||
            voiceId == null)
            return;

        var voiceEv = new TransformSpeakerVoiceEvent(uid, voiceId);
        RaiseLocalEvent(uid, voiceEv);
        voiceId = voiceEv.VoiceId;

        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(voiceId, out var protoVoice))
            return;

        if (args.ObfuscatedMessage != null)
        {
            HandleWhisper(uid, args.Message, args.LexiconMessage, args.LanguageId, args.ObfuscatedMessage, protoVoice.Speaker);
            return;
        }

        HandleSay(uid, args.Message, args.LexiconMessage, args.LanguageId, protoVoice.Speaker);
    }

    private async void OnEntitySpokeToEntity(EntityUid uid, TTSComponent component, EntitySpokeToEntityEvent args)
    {
        var voiceId = component.VoicePrototypeId;
        if (!_isEnabled ||
            args.Message.Length > MaxMessageChars ||
            voiceId == null)
            return;

        var voiceEv = new TransformSpeakerVoiceEvent(uid, voiceId);
        RaiseLocalEvent(uid, voiceEv);
        voiceId = voiceEv.VoiceId;

        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(voiceId, out var protoVoice))
            return;

        HandleDirectSay(args.Target, args.Message, args.LexiconMessage, args.LanguageId, protoVoice.Speaker);
    }

    private async void OnRadioSpokeEvent(RadioSpokeEvent args)
    {
        if (!_isEnabled ||
            args.Message.Length > MaxMessageChars)
            return;

        if (!TryComp(args.Source, out TTSComponent? component))
            return;

        var voiceId = component.VoicePrototypeId;

        if (voiceId == null)
            return;

        var voiceEv = new TransformSpeakerVoiceEvent(args.Source, voiceId);
        RaiseLocalEvent(args.Source, voiceEv);
        voiceId = voiceEv.VoiceId;

        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(voiceId, out var protoVoice))
            return;

        HandleRadio(args.Receivers, args.Message, args.LexiconMessage, args.LanguageId, protoVoice.Speaker);
    }

    private async void OnAnnounceSpokeEvent(AnnounceSpokeEvent args)
    {
        var voiceId = args.Voice;
        if (!_isEnabled ||
            args.Message.Length > _cfg.GetCVar(CCVars.ChatMaxAnnouncementLength) ||
            voiceId == null)
            return;

        if (args.Source != null)
        {
            var voiceEv = new TransformSpeakerVoiceEvent(args.Source.Value, voiceId);
            RaiseLocalEvent(args.Source.Value, voiceEv);
            voiceId = voiceEv.VoiceId;
        }

        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(voiceId, out var protoVoice))
            return;

        Timer.Spawn(6000, () => HandleAnnounce(args.Message, args.LexiconMessage, args.LanguageId, protoVoice.Speaker)); // Awful, but better than sending announce sound to client in resource file
    }

    private async void HandleSay(EntityUid uid, string message, string lexiconMessage, string languageId, string speaker)
    {
        var soundData = await GenerateTTS(message, speaker);
        var soundLexiconData = await GenerateTTS(lexiconMessage, speaker);

        var understanding = _language.GetUnderstanding(languageId);

        if (soundData is null) return;
        if (soundLexiconData is null) return;

        foreach (var session in _playerManager.Sessions)
        {
            if (!understanding.Contains(session))
                RaiseNetworkEvent(new PlayTTSEvent(soundLexiconData, GetNetEntity(uid), languageId: languageId), session);
            else
                RaiseNetworkEvent(new PlayTTSEvent(soundData, GetNetEntity(uid), languageId: languageId), session);
        }

    }

    private async void HandleDirectSay(EntityUid uid, string message, string lexiconMessage, string languageId, string speaker)
    {
        var soundData = await GenerateTTS(message, speaker);
        var soundLexiconData = await GenerateTTS(lexiconMessage, speaker);

        var understanding = _language.GetUnderstanding(languageId);

        if (soundData is null) return;
        if (soundLexiconData is null) return;

        foreach (var session in _playerManager.Sessions)
        {
            if (!understanding.Contains(session))
                RaiseNetworkEvent(new PlayTTSEvent(soundLexiconData, GetNetEntity(uid), languageId: languageId), session);
            else
                RaiseNetworkEvent(new PlayTTSEvent(soundData, GetNetEntity(uid), languageId: languageId), session);
        }
    }

    private async void HandleRadio(EntityUid[] uids, string message, string lexiconMessage, string languageId, string speaker)
    {
        var soundData = await GenerateTTS(message, speaker);
        var soundLexiconData = await GenerateTTS(lexiconMessage, speaker);

        var understanding = _language.GetUnderstanding(languageId);

        if (soundData is null) return;
        if (soundLexiconData is null) return;

        foreach (var uid in uids)
        {
            foreach (var session in _playerManager.Sessions)
            {
                if (!understanding.Contains(session))
                    RaiseNetworkEvent(new PlayTTSEvent(soundLexiconData, GetNetEntity(uid), languageId: languageId), session);
                else
                    RaiseNetworkEvent(new PlayTTSEvent(soundData, GetNetEntity(uid), languageId: languageId), session);
            }
        }
    }

    private async void HandleAnnounce(string message, string lexiconMessage, string languageId, string speaker)
    {
        var soundData = await GenerateTTS(message, speaker);
        var soundLexiconData = await GenerateTTS(lexiconMessage, speaker);

        var understanding = _language.GetUnderstanding(languageId);

        if (soundData is null) return;
        if (soundLexiconData is null) return;

        foreach (var session in _playerManager.Sessions)
        {
            if (!understanding.Contains(session))
                RaiseNetworkEvent(new PlayTTSEvent(soundLexiconData, languageId: languageId), session);
            else
                RaiseNetworkEvent(new PlayTTSEvent(soundData, languageId: languageId), session);
        }
    }

    private async void HandleWhisper(EntityUid uid, string message, string lexiconMessage, string languageId, string obfMessage, string speaker)
    {
        var fullSoundData = await GenerateTTS(message, speaker, true);

        var lexiconSoundData = await GenerateTTS(lexiconMessage, speaker, true);

        // var obfSoundData = await GenerateTTS(obfMessage, speaker, true);
        // if (obfSoundData is null) return;
        // var obfTtsEvent = new PlayTTSEvent(obfSoundData, GetNetEntity(uid), true);

        if (fullSoundData is null) return;
        if (lexiconSoundData is null) return;

        var fullTtsEvent = new PlayTTSEvent(fullSoundData, GetNetEntity(uid), languageId: languageId);
        var fullTtsLexiconEvent = new PlayTTSEvent(lexiconSoundData, GetNetEntity(uid), languageId: languageId);

        var understanding = _language.GetUnderstanding(languageId);

        // TODO: Check obstacles
        var xformQuery = GetEntityQuery<TransformComponent>();
        var sourcePos = _xforms.GetWorldPosition(xformQuery.GetComponent(uid), xformQuery);
        var receptions = Filter.Pvs(uid).Recipients;
        foreach (var session in receptions)
        {
            if (!session.AttachedEntity.HasValue) continue;
            var xform = xformQuery.GetComponent(session.AttachedEntity.Value);
            var distance = (sourcePos - _xforms.GetWorldPosition(xform, xformQuery)).Length();
            if (distance > ChatSystem.VoiceRange * ChatSystem.VoiceRange)
                continue;

            if (!understanding.Contains(session))
                RaiseNetworkEvent(fullTtsLexiconEvent, session);
            else
                RaiseNetworkEvent(fullTtsEvent, session);

        }
    }

    // ReSharper disable once InconsistentNaming
    private async Task<byte[]?> GenerateTTS(string text, string speaker, bool isWhisper = false)
    {
        var textSanitized = Sanitize(text);
        if (textSanitized == "") return null;
        if (char.IsLetter(textSanitized[^1]))
            textSanitized += ".";

        var ssmlTraits = SoundTraits.RateFast;
        if (isWhisper)
            ssmlTraits = SoundTraits.PitchVerylow;
        var textSsml = ToSsmlText(textSanitized, ssmlTraits);

        return await _ttsManager.ConvertTextToSpeech(speaker, textSsml);
    }
}
