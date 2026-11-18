using System.Collections.Generic;
using UnityEngine;

using Ros.Config;
// 人机技能栏：只做选栏与出招顺序；冷却与库存由实体的 EntitySkillController 记账
public class RobotSkillBar
{
    private readonly PlayerEntityData owner;
    private readonly DecisionMachine machine;
    private readonly List<int> slots = new();

    public RobotSkillBar(PlayerEntityData owner, DecisionMachine machine)
    {
        this.owner = owner;
        this.machine = machine;
    }

    public int Capacity => owner.attributes.WeaponSlotCount;

    public IReadOnlyList<int> Slots => slots;

    // 最近一次成功出手是空手普攻：连招窗口内可续下一段，放技能后立即失效
    public bool PunchChain { get; private set; }

    // 持有表变化（珊瑚掉落 / GM / 复活继承）后重排：品质降序，同品质 id 降序
    public void Rebuild()
    {
        var held = owner.skillController.GetSkillIds();
        held.Sort(ComparePriority);

        slots.Clear();
        int capacity = Capacity;
        for (int i = 0; i < held.Count && slots.Count < capacity; i++) slots.Add(held[i]);
    }

    private static int ComparePriority(int a, int b)
    {
        var ca = SkillConfig.Get(a);
        var cb = SkillConfig.Get(b);
        int ra = SkillConfig.QualityRank(ca), rb = SkillConfig.QualityRank(cb);
        if (ra != rb) return rb.CompareTo(ra);
        return b.CompareTo(a);
    }

    // 栏内从高到低 → 空手三连；全不可用返回 false（调用方原地站立）
    // 够不着目标一律不出招（射程读配表），避免射程外空放白扣库存；射程 0 = 只影响自身，与距离无关
    public bool TryCastAny(EntityData target)
    {
        float dist = target != null ? FlatDistance(target) : 0f;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!CanReach(slots[i], dist)) continue;
            if (!TryCast(slots[i])) continue;
            PunchChain = false;
            return true;
        }

        // 普攻同样取配表射程，够不到就交给决策层继续前压
        if (dist > SkillConfig.Get(GlobalConfig.unarmed_punch).castRange) return false;
        if (!TryCast(GlobalConfig.unarmed_punch)) return false;
        PunchChain = true;
        return true;
    }

    // 只影响自身的技能（射程 0）不看距离，其余必须覆盖当前距离
    private static bool CanReach(int skillId, float dist)
    {
        if (!SkillManager.TryGet(skillId, out var skill)) return false;
        return skill.CastRange <= 0f || dist <= skill.CastRange;
    }

    private float FlatDistance(EntityData target)
    {
        Vector3 d = target.transform.position - owner.transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    private bool TryCast(int skillId) => machine.TryUseSkill(skillId);
}
