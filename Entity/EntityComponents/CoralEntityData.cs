using UnityEngine;

using Ros.Config;

public class CoralEntityData : EntityData
{
    public override void OnKilled()
    {
        SpreadPlaguePollute(); // 必须在实体销毁（OnDestroyed 会清 Buff）之前结算
        base.OnKilled();
        var battle = Tool.BattleManager;
        if (battle == null) return;

        battle.OnCoralDestroyed(this); // 击杀者回血
    }

    // 瘟疫污染：被打碎时按自身污染层数给附近进攻方单位挂瘟疫标记，归属施加污染的瘟疫使者
    private void SpreadPlaguePollute()
    {
        if (Tool.BattleManager == null) return;
        if (effectController == null || !effectController.HasEffect(EffectType.PlaguePollute)) return;
        int level = effectController.GetLevel(EffectType.PlaguePollute);
        var source = BattleManager.GetEntity(effectController.GetSourceId(EffectType.PlaguePollute));
        if (source == null) return;
        Vector3 center = transform.position;
        float radius = GlobalVariable.F(global_variablesEnum.buff_plague_pollute_radius);
        float sqr = radius * radius;
        foreach (var entity in BattleManager.EntityContainer.Entities)
        {
            if (entity == null || !entity.Alive || entity.camp != EntityCamp.Attack) continue;
            if ((entity.transform.position - center).sqrMagnitude > sqr) continue;
            if (entity.effectController == null) continue;
            entity.effectController.AddEffect(EffectType.PlagueMark, level, GlobalVariable.F(global_variablesEnum.buff_plague_mark_duration), source);
        }
    }
}
