// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Robust.Shared.Serialization;
using Content.Shared.Actions;
using Robust.Shared.Utility;

namespace Content.Shared.DeadSpace.Languages;

public sealed partial class SelectLanguageActionEvent : InstantActionEvent
{

}

[Serializable, NetSerializable]
public sealed partial class RequestLanguageMenuEvent : EntityEventArgs
{
    public readonly List<string> Prototypes = new();
    public int Target { get; }
    public RequestLanguageMenuEvent(int target, List<string> prototypes)
    {
        Target = target;
        Prototypes = prototypes;
    }
}

[Serializable, NetSerializable]
public sealed partial class SelectLanguageEvent : EntityEventArgs
{
    public string PrototypeId { get; }
    public int Target { get; }
    public SelectLanguageEvent(int target, string prototypeId)
    {
        Target = target;
        PrototypeId = prototypeId;
    }
}
