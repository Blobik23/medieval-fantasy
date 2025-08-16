// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Server.DeadSpace.Medieval.Skill.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server.DeadSpace.Medieval.Skill;

public sealed class MeleeSkillSystem : NeededSkillSystem
{
    [Dependency] private readonly SkillSystem _skillSystem = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeSkillComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(EntityUid uid, MeleeSkillComponent component, ref MeleeHitEvent args)
    {
        float bestModifier = component.DefaultModifier;

        foreach (var skill in component.Skills)
        {
            if (_skillSystem.CnowThisSkill(args.User, skill))
            {
                if (component.DamageModifiers.TryGetValue(skill, out var modifier))
                    bestModifier = Math.Max(bestModifier, modifier);
            }
        }

        args.BonusDamage = args.BaseDamage * bestModifier;
    }
}
