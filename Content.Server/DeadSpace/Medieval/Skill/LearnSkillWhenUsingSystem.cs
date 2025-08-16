// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Server.DeadSpace.Medieval.Skill.Components;
using Content.Server.Inventory;
using Content.Server.Popups;
using Content.Shared.DeadSpace.Medieval.Skills.Events;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Server.Audio;

namespace Content.Server.DeadSpace.Medieval.Skill;

public sealed class LearnSkillWhenUsingSystem : NeededSkillSystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SkillSystem _skillSystem = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LearnSkillWhenUsingComponent, UseInHandEvent>(OnUseInHand, after: new[] { typeof(OpenableSystem), typeof(ServerInventorySystem) });
        SubscribeLocalEvent<LearnSkillWhenUsingComponent, LearnDoAfterEvent>(OnDoAfter);
    }

    private void OnUseInHand(EntityUid uid, LearnSkillWhenUsingComponent component, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager,
            args.User,
            TimeSpan.FromSeconds(component.Duration),
            new LearnDoAfterEvent(),
            eventTarget: uid,
            target: args.User,
            used: uid)
        {
            BreakOnHandChange = false,
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = 0.01f,
            DistanceThreshold = 1f
        };

        int unknown = 0;

        foreach (var skill in component.Skills)
        {
            if (_skillSystem.CanLearn(args.User, skill))
                unknown += 1;
        }

        if (unknown > 0)
            _doAfter.TryStartDoAfter(doAfterArgs);
        else
        {
            _popup.PopupEntity(Loc.GetString("skill-canlearn-gained"), args.User, args.User);
            return;
        }

        if (component.Sound != null)
            _audio.PlayPvs(component.Sound, uid);
    }

    private void OnDoAfter(EntityUid uid, LearnSkillWhenUsingComponent component, LearnDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !EntityManager.EntityExists(args.Used))
            return;

        foreach (var skill in component.Skills)
        {
            _skillSystem.AddSkillProgress(args.User, skill, component.Points[skill]);
        }

        if (component.Sound != null)
            _audio.PlayPvs(component.Sound, uid);

        args.Repeat = true;
    }

}
