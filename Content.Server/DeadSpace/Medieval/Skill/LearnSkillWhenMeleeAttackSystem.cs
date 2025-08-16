// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Server.DeadSpace.Medieval.Skill.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Whitelist;

namespace Content.Server.DeadSpace.Medieval.Skill;

public sealed class LearnSkillWhenMeleeAttackSystem : EntitySystem
{
    [Dependency] private readonly SkillSystem _skillSystem = default!;
    [Dependency] private readonly EntityWhitelistSystem _entityWhitelist = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LearnSkillWhenMeleeAttackComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(EntityUid uid, LearnSkillWhenMeleeAttackComponent component, MeleeHitEvent args)
    {
        foreach (var entity in args.HitEntities)
        {
            if (args.User == entity)
                continue;

            if (component.Whitelist != null && _entityWhitelist.IsValid(component.Whitelist, entity))
            {
                foreach (var skill in component.Skills)
                {
                    if (_skillSystem.CanLearn(uid, skill))
                        _skillSystem.AddSkillProgress(args.User, skill, component.Points[skill]);
                }
            }
            else
            {
                foreach (var skill in component.Skills)
                {
                    if (_skillSystem.CanLearn(uid, skill))
                        _skillSystem.AddSkillProgress(args.User, skill, component.Points[skill]);
                }
            }
        }
    }

}
