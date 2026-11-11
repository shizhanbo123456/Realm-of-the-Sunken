// 基础属性容器：只承载 entity.csv 的六项基础值（首页属性表展示用）；实际属性见 RuntimeEntityAttributes
[System.Serializable]
public class EntityAttribute
{
    public float health = 1000f;
    public int attack = 100;
    public int critRate = 5;
    public int critDamage = 100; // 整数百分比：100 = 与不暴击相同，200 = 翻倍
    public float viewDistance = 80f;
    public int weaponSlotCount = 3;
}

public enum AttrField
{
    Health,
    Attack,
    CritRate,
    CritDamage,
    ViewDistance,
    WeaponSlotCount,
}
