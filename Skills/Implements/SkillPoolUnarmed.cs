using Ros.Transport;

namespace Ros.Skill
{
    // 空手/持械普攻：判定球取当前武器预制体，移动与静止共用同一个技能
    public static class SkillPoolUnarmed
    {
        public static void RegisterAll()
        {
            SkillManager.Register(new SkillPunch());
        }
    }

    public class SkillPunch : SkillBase
    {
        public override int Id => (int)SkillType.SkillPunch;

        protected override void OnCast(EntityData caster)
            => StrikeWeapon(caster, BuildAttack(caster));
    }
}
