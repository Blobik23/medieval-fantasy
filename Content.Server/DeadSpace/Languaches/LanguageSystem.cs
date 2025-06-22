// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Robust.Shared.Random;
using Content.Shared.DeadSpace.Languages.Prototypes;
using Content.Shared.DeadSpace.Languages.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Actions;
using Robust.Shared.Player;
using Content.Shared.DeadSpace.Languages;
using Robust.Server.Player;

namespace Content.Server.DeadSpace.Languages;

public sealed class LanguageSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LanguageComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<LanguageComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<LanguageComponent, SelectLanguageActionEvent>(OnSelect);

        SubscribeNetworkEvent<SelectLanguageEvent>(OnSelectLanguage);
    }

    private void OnComponentInit(EntityUid uid, LanguageComponent component, ComponentInit args)
    {
        _actionsSystem.AddAction(uid, ref component.SelectLanguageActionEntity, component.SelectLanguageAction);
    }

    private void OnShutdown(EntityUid uid, LanguageComponent component, ComponentShutdown args)
    {
        _actionsSystem.RemoveAction(uid, component.SelectLanguageActionEntity);
    }

    private void OnSelect(EntityUid uid, LanguageComponent component, SelectLanguageActionEvent args)
    {
        if (args.Handled)
            return;

        if (EntityManager.TryGetComponent<ActorComponent?>(uid, out var actorComponent))
        {
            var ev = new RequestLanguageMenuEvent(uid.Id, component.LanguagesId);

            ev.Prototypes.Sort();
            RaiseNetworkEvent(ev, actorComponent.PlayerSession);
        }

        args.Handled = true;
    }

    private void OnSelectLanguage(SelectLanguageEvent msg)
    {
        if (EntityManager.TryGetComponent<LanguageComponent>(new EntityUid(msg.Target), out var language))
            language.SelectedLanguage = msg.PrototypeId;

    }

    public string ReplaceWordsWithLexicon(string message, string prototypeId)
    {
        if (!_prototypeManager.TryIndex<LanguagePrototype>(prototypeId, out var languageProto))
            return message;

        var lexiconWords = languageProto.Lexicon;

        if (lexiconWords == null || lexiconWords.Count == 0)
            return message;

        var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(words[i]))
            {
                var randIndex = _random.Next(lexiconWords.Count);
                words[i] = lexiconWords[randIndex];
            }
        }
        return string.Join(' ', words);
    }

    public List<string>? GetKnowsLanguage(EntityUid entity)
    {
        if (TryComp<LanguageComponent>(entity, out var language))
            return language.LanguagesId;
        else
            return null;
    }

    public bool KnowsLanguage(EntityUid receiver, string senderLanguageId)
    {
        var languages = GetKnowsLanguage(receiver);

        if (languages == null)
            return false;

        return languages.Contains(senderLanguageId);
    }

    public ICommonSession[] GetUnderstanding(string languageId)
    {
        var understanding = new List<ICommonSession>();

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity == null)
                continue;

            if (TryComp<LanguageComponent>(session.AttachedEntity, out var lanquage) && KnowsLanguage(session.AttachedEntity.Value, languageId))
            {
                Console.WriteLine(session.AttachedEntity + " знает " + languageId);
                understanding.Add(session);
            }
        }

        return understanding.ToArray();
    }

}
