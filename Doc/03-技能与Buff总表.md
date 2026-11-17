# 03 技能与 Buff 总表（唯一权威）

> 技能 id、名称、行为、发射点/落点、特效、Buff 数值口径**只在本文件维护**，其它文档只放指针。
> 代码实现细节见《02-代码架构》；特效资产编号见《04-特效资产》。

## 一、技能总表（80 个）

### 1.1 武器技能总览（0~49）

> 技能与 id 的对应由 `SkillTypes` 枚举决定：段首显式锚定、段内连续、段间留空供插入。区间：刀 0~10 / 长枪 11~18 / 枪械 19~33 / 魔法球 34~49 / 防守方 60~77 / 鲛人 90~94 / 防御塔 110~112 / 中立雕像 113 / 空手 130~132。措辞取《特效清单分别表1019》的定稿描述。
> **发射点**：全部走实体骨骼锚点（`UpCenter` / `UpFront`；抛出武器、回旋武器用 `RightHand`）。**落点**：不索敌 = 朝自身面向、水平距离 = 技能射程、高度与起点同高；夹角 > 0 的 = 朝范围内最近敌人的体中心，水平距离仍取技能射程、高度取体中心；**2 个雕像孢子例外：直接落到目标身上（体中心 / 脚下），不按射程定距**（范围内无敌人时退回「朝面向 × 射程」）。距离一律从施法者自身位置起算，不从发射锚点起算。

#### 近战刀（0~10）

| id | 名称 | 技能类 | 本体特效 | 状态表现（buff 由客户端统一播） |
| --- | --- | --- | --- | --- |
| 0 | 霜棱散 | SkillFanIceCone | Bullet[18] | 命中挂 1 级减速 5s |
| 1 | 雷锥连闪 | SkillTripleThunderCone | Bullet[19] | 命中挂麻痹 2s |
| 2 | 凝霜弹 | SkillFreezeShot | Bullet[21] | 附加冰冻 2s |
| 3 | 蚀力矢 | SkillStrengthBreakShot | Bullet[22] | 2 级攻击降低 20s |
| 4 | 冰扇双舞 | SkillDoubleFanShot | Bullet[39] | 命中挂 1 级减速 5s |
| 5 | 回锋环 | SkillOrbitBlades | Bullet[40] ×4（绕身） | 无 |
| 6 | 退锋 | SkillBackstepShot | Bullet[41] | 短暂闪避 buff |
| 7 | 腾跃散华 | SkillLeapScatter | Bullet[57] ×N（随机角） | 无 |
| 8 | 掷兵 | SkillThrowWeapon | 武器模型本体 | 无 |
| 9 | 五刃扇 | SkillFanWeaponThrow | 武器模型本体 ×5 | 无 |
| 10 | 天诛落刃 | SkillSkyfallWeapon | 武器模型本体（天降） | 无 |

#### 长枪（11~18）

| id | 名称 | 技能类 | 本体特效 | 状态表现（buff 由客户端统一播） |
| --- | --- | --- | --- | --- |
| 11 | 回旋枪 | SkillBoomerangWeapon | 武器模型本体（去-回） | 无 |
| 12 | 背水一击 | SkillBackstabWeapon | 武器模型本体 + 悬停 | 命中挂麻痹 2s |
| 13 | 疾风步 | SkillSwiftAura | 无 | 1 级加速 20s |
| 14 | 旋枪荡 | SkillOrbitWeaponKnockback | 武器模型本体 ×4（绕身） | 无 |
| 15 | 短距突进 | SkillShortDash | 无 | 无 |
| 16 | 铁壁 | SkillShieldTen | 无（护盾 buff 自带） | 10 级护盾 60s |
| 17 | 裂地踢 | SkillFlyingKick | 无 | 无 |
| 18 | 霜锋 | SkillIceFanTiny | Bullet[9] | 命中挂冰冻 |

#### 枪械（19~33）

| id | 名称 | 技能类 | 本体特效 | 状态表现（buff 由客户端统一播） |
| --- | --- | --- | --- | --- |
| 19 | 背袭弹幕 | SkillBackVolley | Bullet[11] ×6 | 无 |
| 20 | 霜雨扫射 | SkillIceSweep | Bullet[20] | 附加冰冻 3s |
| 21 | 缚足弹 | SkillRootShot | Bullet[28] | 命中挂定身 3s |
| 22 | 浮游弹 | SkillDriftOrb | Bullet[29] | 命中击退 |
| 23 | 冰潮递进 | SkillTripleWaveIce | Bullet[45] | 命中挂冰冻 2s |
| 24 | 前压弹幕 | SkillForwardSpray | Bullet[46] | 无 |
| 25 | 禁声弹 | SkillSilenceBolt | Bullet[47] | 1 级沉默 12s |
| 26 | 天罗 | SkillSkyfallVolley | RangeMagic[2] ×5（自身周围落下） | 无 |
| 27 | 环射 | SkillOrbitWeaponVolley | 武器模型本体 ×N（环身飞出） | 无 |
| 28 | 兵雨 | SkillOrbitWeaponRain | 武器模型本体 ×N（自身周围落下） | 无 |
| 29 | 引力锤击 | SkillPullSmash | RangeMagic[7] 起手 + RangeMagic[4] 落点 | 无 |
| 30 | 坠落序曲 | SkillRainBarrage | Bullet[44] ×N | 无 |
| 31 | 疾风突进 | SkillDashSpeed | 无 | 2 级加速 10s |
| 32 | 同心壁垒 | SkillAllyShieldEight | 无（护盾 buff 自带） | 8 级护盾 60s（自身+附近友方） |
| 33 | 自守壁垒 | SkillShieldFour | 无（护盾 buff 自带） | 4 级护盾 10s |

#### 魔法球（34~49）

| id | 名称 | 技能类 | 本体特效 | 状态表现（buff 由客户端统一播） |
| --- | --- | --- | --- | --- |
| 34 | 终裂 | SkillSplitShot | Bullet[0] 母弹 + Bullet[36] ×5 分裂 | 无 |
| 35 | 引力弹 | SkillPullBolt | Bullet[1] | 命中挂 MotionAttract 牵引 |
| 36 | 静滞矩阵 | SkillStaticBarrage | Bullet[2] ×12（定点） | 命中挂定身 10s |
| 37 | 霜封扇 | SkillFanFreeze | Bullet[3] | 命中挂冰冻 4s |
| 38 | 毒幕往复 | SkillSweepPoison | Bullet[4] ×N | 3 级中毒 5s |
| 39 | 连弹 | SkillBounceShot | Bullet[5]（9 点折线） | 无 |
| 40 | 焚幕 | SkillSweepBurn | Bullet[8] | 5 级燃烧 5s |
| 41 | 六芒连射 | SkillCircleVolley | MagicCircle[7] 法阵 + Bullet[12] ×6 | 无 |
| 42 | 钩影扇 | SkillHookFan | Bullet[14] ×5 | 命中挂 MotionAttract 牵引 |
| 43 | 涡爆 | SkillVortexBomb | RangeMagic[1] | 命中挂 MotionAttract 牵引 |
| 44 | 六点灼阵 | SkillSixOrbs | RangeMagic[3] ×6（定点） | 无 |
| 45 | 禁言之落 | SkillSkyfallSilence | RangeMagic[4]（头顶落下） | 命中挂沉默 10s |
| 46 | 爆裂崩击 | SkillBlastKnockback | Bullet[7] ×6（扇形） | 灼烧 3 级 10s |
| 47 | 弧刃背袭 | SkillBackArcWeapons | 武器模型本体 ×N（贝塞尔） | 无 |
| 48 | 锐意共鸣 | SkillAllyCritBoost | 无 | 5 级暴击率 + 5 级暴伤 20s（自身+附近友方） |
| 49 | 圣域壁垒 | SkillAllyShieldTwentyFive | 无（护盾 buff 自带） | 25 级护盾 30s（全体友方） |

### 1.2 防守方 6 角色（60~77）

> 这 18 个技能**除山崩连踏（61，播 RangeMagic[5]）外都不播技能本体特效**：状态表现由对应的 Buff 承担，客户端按 `Config.buff_vfx` 统一播（技能里禁止播 buff 特效）。

| id | 名称 | 技能类 | 本体特效 | 状态表现 |
| --- | --- | --- | --- | --- |
| 60 | 磐石壁垒 | SkillRockShield | 无（护盾 buff 自带） | 附近水晶雕像挂 StatueShield；附近无水晶雕像则给自己 |
| 61 | 山崩连踏 | SkillPowerSuppress | RangeMagic[5]（自身位置 ×3） | 命中挂 2 级 AnimSlowDown 10s |
| 62 | 焚塔灵焰 | SkillCrystalStatueBlazeCast | 无 | 全场水晶雕像挂 StatueBlaze |
| 63 | 白瞳凝视 | SkillEyeMark | 无 | EyeMark（小地图常显） |
| 64 | 夜隐 | SkillNightMark | 无 | 自身挂 NightMark（不出现在敌方小地图） |
| 65 | 长夜降临 | SkillForceNight | 无 | 无 |
| 66 | 唤尸 | SkillSummonZombies | 无 | 无（5 只普通鲛人，等级跟随自然刷新等级） |
| 67 | 死境漫游 | SkillDeathStrollCast | 无 | DeathStroll（霸体 + 光环 DoT） |
| 68 | 菁英尸潮 | SkillSummonElites | 无 | 无（3 只精英鲛人，等级跟随自然刷新等级） |
| 69 | 缄默之咒 | SkillSilenceCast | 无 | 自身 8m 内敌人挂 Silence |
| 70 | 沉沼缚 | SkillMireCast | 无 | 全场敌人挂 Mire |
| 71 | 荆棘回响 | SkillReflectCast | 无 | 守护雕像挂 Reflect |
| 72 | 疫印 | SkillPlagueMarkCast | 无 | 自身 8m 内敌人挂 PlagueMark |
| 73 | 疫染 | SkillPlaguePollute | 无 | 场上现有珊瑚挂 PlaguePollute；珊瑚被摧毁时给 8m 内进攻方挂 1 层 PlagueMark |
| 74 | 疫爆 | SkillDetonatePlague | 无 | 移除 PlagueMark 并结算伤害 |
| 75 | 苍白耀光 | SkillPaleLightCast | Bullet[2] ×5 | 命中挂 PaleLight |
| 76 | 苍白蚀影 | SkillPaleDarkCast | Bullet[1] ×5 | 命中挂 PaleDark |
| 77 | 蔽目之雾 | SkillFogCast | 无 | 敌人挂 Fog |

**被动（不是技能、不占 id）**：暴击麻痹（`BattleManagerPassive.OnHitPassive`）、单次受伤不超过生命上限 5%（`AdjustIncomingDamage`）、命中类伤害结算后生命低于上限 10% 即挂 20s 10 级护盾并恢复 1000 生命值（`CheckLowHealthShield`，每角色 60s 一次）、光暗转化（`CheckPaleConversion`）、教皇守护 / 鲛人刷新等级提升等全局被动见策划案第二十章。

### 1.3 非玩家单位与空手（90~132）

| id | 名称 | 技能类 | 本体特效 | 状态表现 |
| --- | --- | --- | --- | --- |
| 90 | 腐爪·右 | SkillZombieClawR | 无（手部骨骼判定球） | 无 |
| 91 | 腐爪·左 | SkillZombieClawL | 无（手部骨骼判定球） | 无 |
| 92 | 裂骨爪·右 | SkillEliteZombieClawR | 无（手部骨骼判定球） | 无 |
| 93 | 裂骨爪·左 | SkillEliteZombieClawL | 无（手部骨骼判定球） | 无 |
| 94 | 血嚎 | SkillEliteZombieScream | 无 | ZombieScreamBoost（自身 + 同阵营，半径读第16列） |
| 110 | 孢毒喷吐·甲 | SkillCrystalStatueSporeA | Bullet[25] | 无 |
| 111 | 灵火喷吐 | SkillCrystalStatueSporeBlaze | RangeMagic[6]（直接落在目标脚下，无子弹） | 无 |
| 113 | 疫孢吐息 | SkillNeutralStatueSpore | RangeMagic[9] | 命中挂 Poison |
| 130 | 直拳·左 | SkillPunchLeft | 无（手部骨骼判定球） | 无 |
| 131 | 直拳·右 | SkillPunchRight | 无（手部骨骼判定球） | 无 |
| 132 | 崩拳 | SkillPunchSmash | RangeMagic[6] | 无 |

> 水晶雕像 / 中立雕像的孢子同样从**体中心 `UpCenter`** 发出（不走雕像顶部）；落点是**目标自身位置**（110 体中心，111 / 113 脚下），**不按射程定距**——敌人贴到塔脚下也照样落在其身上；范围内无敌人时退回「朝面向 × 射程」。

## 二、Buff 总表（35 条持续表现）

| Buff / 状态（EffectType） | 持续特效 | 主要来源 |
| --- | --- | --- |
| StatueReduce 贤者雕像减伤 | Shield[0] 红覆盖 | 守护雕像机制（存活外围点） |
| StatueShield 岩石护盾 | MC[8] 红色防御增益 | 防守方角色3 主动1 |
| PopeGuard 教皇守护 | Shield[12] 红熔岩 | 防守方角色2 被动 |
| Shield 护盾 | Shield[2] 白厚实 | 光盾 / 枪阵屏障 / 奥术屏障 |
| Stun 麻痹 | Buff[20] 麻痹黄 | 破霸重斩 / 麻痹弹 / 落雷术 / 防守方角色3 被动 |
| StatueBlaze 灵火 | MC[3] 深红护盾 | 防守方角色3 大招 |
| Burning 燃烧 | Buff[11] 火焰 | 燃烧弹 |
| EyeMark 白眼标记 | Buff[23] 宇宙 | 防守方角色0 主动1 |
| NightMark 黑夜标记 | Buff[30] 紫色祝福（与 PlagueMark 共用） | 防守方角色0 主动2 |
| PlaguePollute 瘟疫污染 | Buff[22] | 防守方角色1 主动2（挂珊瑚） |
| PaleLight 苍白之光 | Buff[15] 黄色泛光 | 防守方角色4 主动1 |
| PaleDark 苍白之暗 | Buff[4] 黑色缠绕 | 防守方角色4 主动2 |
| PaleFire 苍白之火 | Buff[3] 血色缠绕 | 防守方角色4 被动：光暗双 8 层转化 |
| PaleIce 苍白之冰 | Buff[14] 绿色治愈 | 防守方角色4 被动：光满 10 层转化 |
| PaleThunder 苍白之雷 | Buff[20] 麻痹黄 | 防守方角色4 被动：暗满 10 层转化 |
| DeathStroll 死灵漫步 | Buff[29] 橙色祝福 | 防守方角色5 主动2 |
| DeathCurse 死灵诅咒 | Buff[0] 紫色雾团 | 防守方角色5 主动2（尚未施加） |
| Silence 沉默 | Buff[2] 科技紫 | 防守方角色2 主动1 |
| Mire 泥沼 | Buff[17] 黑绿喷发 | 防守方角色2 主动2 |
| Reflect 反伤 | MC[6] 深紫色 | 防守方角色2 大招 |
| Root 定身 | Buff[19] 麻痹蓝 | 捕获投掷 / 束缚钉 |
| PlagueMark 瘟疫标记 | Buff[30] 紫色祝福 | 防守方角色1 主动1 |
| Poison 中毒 | Buff[13] 绿色治愈 | 毒珠 / 毒气弹 / 引爆 |
| Freeze 冰冻 | Buff[12] 冻结 | 冻结弹 / 冰锥术 / 冰霜新星 |
| AnimSlowDown 减速 | Buff[17] 黑绿喷发 | 破甲重弹 |
| AnimSpeedUp 加速 | Buff[27] 环绕风 | 激励弹 |
| AngelBless 天使祝福 | MC[9] 蓝色攻击增益 | 攻占天使雕像 |
| DemonBless 恶魔祝福 | MC[9] 蓝色攻击增益 | 攻占恶魔雕像 |
| ZombieScreamBoost 鲛人嘶吼增益（攻击 +20%/级、暴击率 +100 点/级、移速 +20%/级） | MC[9] 蓝色攻击增益 | 精英鲛人·血嚎（自身 + 半径内同阵营） |
| AttrAttackBoost | MC[0] 黄色力场 | 激励法阵等属性增益 |
| AttrCritRateBoost / AttrCritDamageBoost | MC[1] 治愈绿 | 暴击 / 暴伤增益 |
| AttrAttackDecrease / AttrCritRateDecrease / AttrCritDamageDecrease | MC[5] 深绿护盾 | 属性削弱类 |

### 2.1 明确无特效 / 非特效的状态（7）

| 状态 | 处理 |
| --- | --- |
| AttrViewDistanceBoost / AttrViewDistanceDecrease 可见距离增减 | 无特效 |
| DodgeBoost 闪避提升 | 无特效（属性侧暂无闪避字段） |
| BattleFury 愈战愈勇 | 无特效（策划案明确无外观） |
| MinimapLost 小地图失联 | 小地图 UI 逻辑，非特效 |
| GmSpeedUp GM 极速 | 无特效 |
| Fog 迷雾 | 由技能本体挂 Buff[6] 紫雾喷发（不在 `buff_vfx` 表内） |

---

## 三、实现口径

Skills文件夹：
**代码状态**：技能 80 个，按拥有者分文件：`SkillPoolWeapon`（武器 50）、`SkillPoolDefense`（防守方 18）、`SkillPoolNpc`（普通鲛人 2 + 精英鲛人 3 + 防御塔孢子 1 + 中立雕像孢子 1）、`SkillPoolUnarmed`（空手 3），统一由 `SkillPools.RegisterAll()` 注册。`SkillType` 的段首项显式锚定 id、段内按枚举顺序递增，段间留空供插入：刀 0~10 / 长枪 11~18 / 枪械 19~33 / 魔法球 34~49 / 防守方 60~77 / 鲛人 90~94 / 防御塔 110~112 / 中立雕像 113 / 空手 130~132。**段内不可断号**、**0~49 必须整段连续**（`Config.GetRandomWeaponSkillId` 按整段随机）；武器不跟技能绑定——武器由实体在 entity.csv 里固定持有，技能只声明攻击动画，与技能栏槽位无关（槽位顺序由客户端按 skillId 升序排列，玩家可在技能选择面板自选）。技能图标需按当前技能表重建（分配工具待重做）。
[E]外部调用只向SkillManager传入技能id即可。
技能双端执行模型：EntitySkillController.TryUseSkill 是全部释放路径（技能栏按键 / J 键空手 / AI / 非玩家）的唯一收口——释放瞬间调 `skill.BeginCast(entity, skillId)`：先摇攻击动画（类型读 skill.csv 第 12 列，留空则按下即施放），等动画到**攻击帧**（`EntityAnim.onAttack`）才调 `OnCast(caster)` 直接结算（此刻索敌/起点基于位移后的实时位置）；**攻击帧回调不一次性清空**，多段攻击动画的每个攻击帧各结算一次。表现是独立链路：技能在需要客户端复现的地方调 `SendWave(caster, 波次id, 写包)` 发一个**波次包**，客户端收到即经 `SkillManager.PlayWave(context)` 调同一技能的 `PlayWave(context)` 按波次 id 分派播放（不等自己的 onAttack）。动画由 `SkillBase.CastAnim`/`HasCastAnim` 统一读表。技能内禁止直接 Instantiate 特效，也不得引用两个资源管理器的特效列表（下标映射与加载集中在 VfxManager）。

**波次包（SCSkillDisplayContext）**：`SkillId` / `SkillWave` / `caster` 三个固定头 + 256B 自定义 payload（游标与 `length` 在类内，读写原语 `WriteVector3` / `ReadVector3` 等，超长与读越界直接抛）。
① **一个包 = 一次可立即执行的发射事件**，不是整个技能的表现脚本。客户端收到就照做，不推导、不重算后续波次。
② **包粒度由「这一波要不要重新索敌」决定**：多波共用一次索敌 → 只发一个包，客户端用同一个波次常量 + 延时工具排后续波；每波/每发都要重新索敌 → 每波一个包，各自带当时的敌人位置。这是两端一致性的要求，不是性能取舍。
③ **包里只放客户端推不出来的东西**：发射位置、方向/目标点、随机种子。发数、散布角、波间隔、时长、轨迹形状、特效下标一律写死在技能类常量里。伤害范围不写进包：有本体特效的取该特效预制体的命中球，无特效近战取 skill.csv 第 9 列半径；倍率与再伤害间隔取第 11 / 10 列。
④ **payload 布局由每个技能自己决定**：`SendWave` 里的写序与 `PlayWave` 里的读序一一对应，两段代码就在同一个技能类里，改包不会再漏改另一端。
⑤ **跨端随机一律伪随机**：服务器施放时生成种子写进包里，两端用 `SkillSeed.Rng(seed)` 按固定调用顺序取数（禁止 `UnityEngine.Random`：全局状态，两端消耗顺序不同必然发散）。用到种子的技能：腾跃散华、背袭弹幕。
⑥ **纯服务端技能不发包**：只加 buff / 召唤 / 位移 / 增减益的技能在 `OnCast` 里直接干活，没有 `PlayWave`、也没有网络开销——不是靠「是否发送」的开关，而是根本不生成上下文。
⑦ **禁止给子弹加跨端回调**：分裂 / 弹跳 / 延时等二次效果，施放时就算好时刻与点位，两端到点各自创建——弹跳 = 起点 + 8 跳点位整串下发；分裂 = 母弹终点 + 按弹速与距离算出的时刻。
包体参考：头部固定 18B（skillId 1 + 波次 1 + 施法者 id 2 + 施法者位置 12 + payload 长度 2），普通射击再加 24B（起点 + 落点），带种子 +4B，弹跳 9 点 +112B；整包仍受 `EnsTemporaryBuffer` 的 256B 约束。

**发射点**：只用实体骨骼锚点。每个技能在 `OnCast` 里调 `OriginPos(caster, 锚点, 偏移)` 现算：`UpCenter`（26 个）/ `UpFront`（12 个）/ `RightHand`（抛出武器、回旋武器 2 个）；水晶雕像与中立雕像也是 `UpCenter`（不走雕像顶部）。服务器把算出的起点写进波次包，客户端直接用包里那个点，不再按实体重算。
**武器**：技能表里没有武器列，武器由实体固定持有（entity.csv 给编号，动画/显示类型见 weapon.csv，规则见《02-代码架构》「武器显示」）。投掷与绕身武器类（掷兵 / 五刃扇 / 天诛落刃 / 回旋枪 / 背水一击 / 旋枪荡 / 环射 / 兵雨 / 弧刃背袭）掷的就是该实体手上那把武器，命中球与特效下标都跟着武器预制体走。
**冷却与存储（第 5 / 6 列）**：**存储上限最高 6**。小技能（低倍率、瞬发、单体）给 4~6 次、CD 5~10 秒，用释放频率弥补单次伤害；中技能 3~4 次、CD 10~16；大技能 2~3 次、CD 18~30；终极（全场 / 全场支援）1~2 次、CD 40 以上。品质越高，同等体量下 CD 越短。
**伤害倍率（第 11 列）**：单发技能 1.5~3.5、多发或高频技能 0.6~1.2，全场 / 法阵类总量控制在单发 3~4 的等效水平；表中数值与该技能代码里的基础倍率是**乘算**（基础倍率非 1 的技能，表里按「目标总量 ÷ 基础倍率」填）。
**索敌与落点**：索敌开关与范围逐技能读 `skill.csv` 第 13 列夹角 + 第 14 列射程（**没有独立的索敌半径列，半径一律取射程**），代码里没有技能级开关。落点走 `SkillBase.Aim(caster, origin)`：
- 夹角 ≤ 0.1：**完全不索敌**，落点 = 自身位置 + 面向 × 射程，**高度 = 起点高度**（水平射出）；
- 夹角 > 0.1：在自身朝向左右各夹角内（扇区 = 夹角 × 2）锁最近的敌人，水平方向朝目标体中心（`TargetSelect.BodyPos`，即 `UpCenter`），**水平距离仍取射程**、落点高度 = 目标体中心（不锁 `transform.position` / 脚部）；
- 夹角 > 179.9：**不限方向**（360°），雕像孢子与「周围 / 全场」类技能用这个；
- **雕像孢子例外（110 / 111 / 113 等塔与树的孢子）**：索敌成功时落点直接取目标自身位置（110 = 体中心，111 / 113 = 脚下），**不按射程归一化**——塔不需要预判，敌人贴到脚下也照样打在它身上；射程只用于「能不能打」（索敌半径与开火判定）；
- 射程 > 9999：**不限距离**（全场类统一写 10000，填更大值等价）；
- **水平距离从施法者自身位置起算**，不从发射锚点起算：锚点在身前时，终点到身体的距离仍恒等于射程，不会变成「射程 + 锚点偏移」；
- 范围内一个敌人都没有时，落点与「不索敌」完全一致（朝面向、定距射出），**不回退到自身**（雕像孢子同样如此）；
- 射程 = 0 表示只影响自身（自身增益 / 位移 / 自身光环），这类技能不调 `Aim`、不参与索敌，落点由各自轨迹常量决定；
- 专门做上下变化的技能自己覆盖瞄准点（天罗 / 兵雨 → 自身脚下）；不写瞄准点的 6 个（回锋环 / 腾跃散华 / 旋枪荡 / 静滞矩阵 / 六芒连射 / 六点灼阵）落点由各自轨迹常量决定。

`TargetSelect.AimPos(entity, 夹角, 半径, 射程, normalizeDistance)` 给非射击类索敌用（防守方 / 塔 / 树）：`normalizeDistance = true`（默认）时按体中心定向、水平距离取射程；**雕像孢子传 `false`，落点直接取目标体中心**（`TargetSelect.BodyPos`），无敌人时退回「朝面向 × 射程」。

**自动索敌（第 17 列）**：填 1 时，释放瞬间先按射程做一次**不限方向**的最近敌搜索，把施法者立即转向该敌人（写逻辑朝向 yaw + 对齐 `transform.rotation`，无过渡），然后再走瞄准与攻击帧结算 —— 转向在算落点之前，所以「夹角 ≤ 0.1、弹道沿面向」的技能也会朝敌人打出去。射程内没有敌人时保持原朝向。转向只对真有朝向概念的实体生效（玩家 / 鲛人），雕像类不转。人机与 AI 操控的实体同样生效，但它们的朝向随后由自己的决策层接管。
**轨迹构建**：扇形类各调 `Spread.Fan(origin, aim, 发数, 波数, 夹角, 波间隔, 时长, emit)`——`emit` 在服务器是「发一颗子弹」、在客户端是「播一次特效」，两端共用同一套角度展开与波次时序；发数或夹角随波变化的技能用 `shotsOfWave` / `spreadOfWave` 两个可选出参。不走扇形的（环绕 / 回旋 / 天降 / 弹跳 / 分裂 / 环绕扫射等）自己 new 对应轨迹类，轨迹类型见《02-代码架构》「轨迹」节，时长在构造时传入。
**多目标**：客户端表现要用的目标串（如弹跳的 9 个点）由服务器算好后按自己的布局写进 payload；纯服务端结算用的目标集合，走 `CollectEnemies` / `CollectInRange` 收集后当场消费，不进包——这类技能根本不调 `SendWave`。多目标的上限与排序规则写进技能常量。
**命中附加效果**：`AttackData.addEffectEvent`（命中挂 Buff）与 `AttackData.onHit`（命中额外逻辑），由子弹命中管线 `BattleManagerCombat.TryHitBullet` 统一消费（近战也走子弹）。`SkillBase` 提供 `ApplyEffect`（命中挂 Buff 回调）、`GiveEffect`（直接挂 Buff）、`PullTo`（命中拉拽）。**击退不是回调**：力度写在 `AttackData.knockbackPower` 上，由命中管线统一处理（见速度节⑥）。
**持续型 Buff 表现**：客户端按同步的 `SCEntityDisplayInfo.buffs` 增删特效（仅 `includeRuntime || hasBuffs` 的包触发），映射表见 `Config.buff_vfx`（清单即本文件二章）。**技能里禁止再为 Buff 播持续特效**（不写 `PlayFollow`）——否则同一状态叠两份、且技能那份不随 Buff 消失而清理。每个 Buff 特效必须配 `BulletVfxInfo.hits`（至少一段）：客户端特效按需加载，贴合数据从**加载出来的实例**上取 `hits[0]`，把它的竖直范围 `center.y ± radius` 等比缩放到实体的 `EntityModelInfo.yRange`——底对底、顶对顶，x/y/z 同倍率缩放，根节点竖直偏移由两者差值算出；rotation 不调、x/z 不做平移（资产保证 `center.x/z = 0`）。缺 hits 的 Buff 特效在启动自检（服务器侧读 `AssetsManager`）时报错，并在播放时按原始大小兜底。
**尺寸（skill.csv 第 9 列）**：`|` 分隔，按特效出现顺序每项一个值。有特效/武器模型的技能，值 = 该特效的**缩放倍率**（1 = 资产原大小，播放时由 `VfxManager.ApplyScale` 直接写成特效根节点缩放）；近战（腐爪 / 裂骨爪 / 直拳）读武器预制体的命中球，同一列也是命中球的**缩放倍率**。飞踢这类没有本体特效的自我判定值 = **判定半径**（米），由 `SkillBase.MeleeRadius` 直接读表。倍率列表为空按 1 处理，列表非空但下标越界、或飞踢没配半径，一律抛 `FormatException`（无代码兜底）。
**命中判定（`BulletVfxInfo.hits`）**：伤害范围不再存在于攻击数据里（`AttackData` 已无 radius 字段），改为读特效预制体上的命中球——`start`/`end` 是特效出生后的秒数窗口（`end = 0` 表示全程有效），`center` 是相对特效根节点的局部球心（资产位姿归零后即特效实际位置），`radius` 是局部半径。服务器每帧按「上一帧球心 → 本帧球心」的胶囊判定，球心 = 轨迹位置 + 朝向（与客户端 `BulletPlayer.Tangent` 同算法）+ 技能缩放倍率。多段各自独立按窗口结算。**所有伤害技能一律发一颗子弹**（原地范围技能用 `SkillTrajectoryPoint` 定在原地陪特效走），伤害只走命中球。**近战（Combo / 腐爪 / 裂骨爪）发的是隐形子弹**：轨迹 `SkillTrajectoryWeapon` 贴武器实例，命中球只取 `center`/`radius`、忽略资产里的 `start`/`end`，存活固定 0.5s 覆盖整段挥刀，判定球随武器挥动扫过、武器收起后停在最后位置。
**伤害间隔（第 10 列）与段倍率（第 11 列）**：第 10 列 = 同一目标最短再伤害间隔（秒），**留空解析为 100**（相当于只结算一次），需要反复结算的才填值；第 11 列 = 伤害倍率列表，空 = 全 1，只填 1 项 = 所有段共用，填多项 = 按段序取（项数少于段数抛错）。缺命中球的子弹会打一条 `[子弹]` 报错日志（同一特效只报一次），因为它不会造成任何伤害。
**攻击动画（第 12 列）**：填 `EntityAnim.AttackType` 的数值——留空 = 无动作（按下即施放）/ 10 空手攻击 / 11 持械攻击 / 20 空中下落 / 21 空中前冲 / 30 踢 / 31 嘶吼 / 40 / 41 / 42 短 / 中 / 远程大招 / 50~53 武器招式 A~D。**0 = 连招（Combo）只给普通攻击用，技能表不要填**；不需要动作的简单技能优先留空，减少攻击动画对走位的打断。动画由 `SkillBase.CastAnim` / `HasCastAnim` 统一读表；填了不在上表内的值，加载期直接抛 `FormatException`。
**索敌夹角与射程（第 13 / 14 列）**：第 13 列 = 索敌范围夹角（度，实际扇区 = 该值 × 2），`≤0.1` 完全不索敌、`>179.9` 不限方向；第 14 列 = 射程，同时就是索敌半径，`>9999` 不限距离（全场类统一写 10000）。**没有独立的索敌半径列**；夹角 >0.1 时射程必须 >0，为 0 在加载期抛 `FormatException`。射程 0 = 只影响自身（自身增益 / 位移 / 自身光环）。取谁（最近 / 范围内全体 / 链式 / 友方 / 指定物件）仍由技能代码决定，本两列只限定搜索范围。
**六项评分（第 15 列）**：定长结构体，6 项用分号分隔（输出;冷却恢复;控制;支援;攻击范围;攻击速度），取值 0~4 对应 D / C / B / A / S；项数不是 6、非整数或越界，加载期直接抛 `FormatException`；整格留空 = 不评（`SkillScore.Count` 为 0）。只用于 UI 展示（技能栏条目悬浮的 `UI_SkillTip`，每项一行 `UI_TipLevel`，`m_level` 档位 = 评分值），不参与任何战斗计算。
**蓄力与启用蓄力（第 19 列 + AnimEvent「蓄力值」）**：蓄力速率逐动画片段在 Inspector 配（`AnimEvent.chargeRate`，**每秒蓄力增量**，默认 `-1` = 每秒倒扣 1，所以没配蓄力的片段会主动把蓄力散掉）。服务器实体在片段内按真实时间累加，与动画播放倍率无关，运行时钳在 `0 ~ global_variables.charge_max`。**任何一次攻击结算都先记录本次蓄力值、再立即清零**（`SkillBase.Cast`，含无攻击动画的瞬发技能与普攻，与技能是否启用蓄力无关）；第 19 列填 1 的技能才把记录值乘进击退：`击退力度 × (蓄力值 + 1)`，上限即 `charge_max + 1` 倍。多段攻击帧只有第一帧拿得到蓄力值，后续帧读到 0。留空 = 不启用，击退仍是配表原值。
**位移**：统一走 `MotionBase`（`Entity/Motion/`），由 `EntityData.TickVelocity` 逐帧驱动（Enter / Update / 到期 Exit 三态都在 TickVelocity 内，见速度节⑤），禁止直接改 position 以免穿模；现有 `MotionDash`（恒定朝前）、`MotionToPoint`（固定点）、`MotionAttract`（持续被目标吸引，距离越远速度越大、上限封顶；目标可传 `Transform` / 固定点 / 位置来源——服务器子弹没有 Transform，用「轨迹 + 出生时间」算实时位置）。
**延时 / 多段 / 重复结算：统一用 `Timer.AddTimer`**（见 Utils 节），不要在技能里自己写计时字段——多段命中、延时爆炸、引导间隔、姿态持续期结束等都在 `OnCast` / `PlayWave` 里注册即可。`AddTimer` 没有取消句柄，注册的闭包请自行判空（实体可能已销毁）；需要中途取消的过渡动画用 `Timer.AddTransition`（有句柄）。

Buff管理器（EntityEffectController）设计要点（对应策划案 11.3）：
1. **统一添加入口 `AddEffect(EffectType type, int level = 1, float duration = -1f, EntityData source = null)`**：外部只给类型 / 等级 / 持续时间（<0 = 永久）+ 施加者实体，**不传任何数值**——强度全部由控制器内部定死（系数进 Config 通用 Buff region）。`source` 非空时克隆其 `floatingAttribute` 存为**施加者属性快照**（独立实例，施加者后续被减益不影响已挂 Buff），`sourceId` 由此自动取（DoT 击杀归属）；无施加者的强加（守护雕像减伤/愈战愈勇/小地图失联等）传 null。
   **叠加语义由 effect.csv「叠加模式」列决定**（`EffectConfig.stackMode`，留空属配表错配直接抛）：**0 = 高等级覆盖低等级、相同等级刷新时间**（低于当前等级不生效，也不刷新时间）；**1 = 多层分别生效、分别计算剩余时间**（每次施加压一层，各层自带等级 / endTime / nextTickTime / 施加者快照 / 护盾值）；**2 = 刷新时间并叠加等级**（同一条内累加）；**3 = 强制 1 级，重复添加仅取最大时间**。
   数据按 `Dictionary<EffectType, List<EffectLayer>>` 组织，每层带自增 `serial`，到期或被扣光按 id 精确移除；查询侧 `GetLevel` = 各层等级之和、`GetSourceId` 取最新一层、`GetRemainTime` 取剩余最长的一层。施加者与快照以最新一次生效的施加为准。
2. 双轨属性，事件驱动：只有属性修改类 Buff（`IsAttribute` 白名单，**4 对 Boost/Decrease**：攻击 / 暴击率 / 暴击伤害 / 可见距离）允许改属性。幅度内部定死（方向由 Boost/Decrease 类型表达）：**攻击 / 可见距离 = 被施加者对应属性初始值 × 20% × 等级**（`Config.buff_attr_ratio`）；**暴击率 / 暴击伤害 = 固定加点 × 等级**（`Config.buff_crit_rate_points` 10 点 / `Config.buff_crit_damage_points` 20 点），线性叠加、不随初始值放大（200% 暴伤挂 1 级 = 220%）；**武器槽位数不可被 Buff 改变**，生命值上限也只由升级路线改变。
   **`EntityAttribute.health` 一个字段承载两种语义**：在 `baseAttribute` 里是**生命值上限**（配置项，entity.csv 每行只有一个"生命"列，由升级路线抬高），在 `floatingAttribute` 里是**当前生命值**（受击扣减、生成/复活克隆即满血、死亡清零）。→ **读上限读 `baseAttribute.health`，读当前生命读 `floatingAttribute.health`。**
   Add/Remove 时统一重算运行时属性（base 克隆 + Σ 各层修改量，再把当前生命**夹回上限**——夹取不平移 = 不自动回血）；其它 Buff 一律不得触碰属性
3. 查询式战斗管线：功能 Buff 不做每帧计算，由战斗代码在明确时机调用查询接口——受伤管线顺序 = 护盾吸收（**仅命中类伤害，即正常/暴击**；盾不够的溢出照常扣血）→ 减伤 → 受伤乘区（愈战愈勇；固定数值伤害跳过）→ 扣血 → 反伤；DoT / 反伤 / 瘟疫引爆等特殊伤害**无视护盾直扣血**；出伤乘区由攻击方在伤害计算时调用
4. OnUpdate 只做两件事：按层到期移除（每层记录自己的 endTime，不每帧刷新剩余时间）、固定间隔 tick（`IsTicking`：燃烧/苍白之火/中毒/死灵漫步/天使祝福/恶魔祝福，**逐层各自 1s 一跳**，伤害类吃减伤、无视护盾、不吃增减伤，两个祝福每跳按固定量回血并夹到生命上限）。**每跳伤害也内部定死**：燃烧与苍白之火 = `Config.burning_damage_per_tick`(30) × 该层等级；中毒 = `Config.poison_damage_ratio`(0.5) × max(该层施加者快照力/魔) × 该层等级；死灵漫步 = `Config.death_stroll_dot_ratio`(1.5) × max(快照力/魔) × 该层等级，光环半径 `Config.death_stroll_radius`。**叠层自动提高每跳伤害**
5. 护盾（`Shield`/`StatueShield`，effect.csv「特殊计算效果」= 1）：**每层各有一份抵消量**，初始值 = 被施加者生命上限 × `global_variables` 的 `buff_shield_health_ratio`(0.1) × 该层等级；**只对命中类伤害（正常/暴击）生效**——按 `min(盾值, 本次伤害)` 扣盾（**剩余时间最短的层优先**，扣空即移除该层），盾不够的溢出部分照常扣血；撞盾的命中一律飘「护盾」页，数值取名义伤害（整次数值）。DoT / 反伤 / 瘟疫引爆无视护盾、也不消耗护盾。反伤（`Reflect`）按比例制：**反弹 = 实际受伤（扣盾/减伤后）× 等级 × `Config.reflect_rate`(0.1)，向下取整 min=1；实伤 ≤1 不反弹**
6. 极性静态标注：`EffectType.IsNegative()` 扩展方法固定每种 Buff 的正/负（负面白名单 + 全部属性削弱 Decrease 类），`AddEffect` 不接收极性参数；净化（`RemoveAllNegative`）按此筛选
7. 控制与霸体全部由 effect.csv 列驱动：`禁止移动输入` → `CanMove` = false、`禁止技能输入` → `CanCastSkill` 校验、`冻结动画` → 动画播放速度置 0（当前攻击随之被打断，所有带该列的层移除后恢复）、`霸体等级覆盖` → `GetEndureLevel` 优先返回该值（留空不覆盖，多层取最高）。`GetEndureLevel` 顺序：effect.csv 霸体覆盖 > 强控 → 无霸体 > 动画片段（`AnimEvent.Endure`）> 配表「霸体等级」下限。**霸体只有两档（无霸体 / 霸体）**，没有中间档；**冰冻 / 苍白之冰**（覆盖 1）期间虽不能行动，霸体仍是霸体（不被打断、不进受击；击退不受霸体影响，照常生效）
8. 破霸体：攻击数据（AttackData.breakEndure）命中带霸体的目标时进入受击；不破霸体的攻击只对无霸体目标生效，打中霸体目标不进受击、不停位移。进入受击则移除 MotionBase，并播受击动画（破霸体 = 长受击，否则短受击）+ 转身面向命中来源。**击退与受击解耦**：霸体与冰冻都不阻断击退，唯一阻断项是护盾（身上有任意护盾层则完全不推），且击退与受击动画并行。命中判定入口 = EntityData.ProcessHit（受击与打断位移在 enterHit 分支内，击退在其外，见速度节⑥）
9. 沉默/强控期间无法释放技能（EntitySkillController.TryUseSkill 校验 `CanCastSkill` = 无「禁止技能输入」且无「禁止移动输入」的 Buff）
10. Buff 数据使用 struct；标记类 Buff（瘟疫标记/苍白之光/暗）无自身效果，供其它逻辑读层数（苍白转化由被动订阅 Buff 变化实现；**转化出的苍白之冰/之雷/之火归属原施加者**——`AddEffect` 的苍白钩子把标记的施加者传给 `CheckPaleConversion`，多人叠同一目标时归属最后叠层者）。苍白之冰/之雷是独立强控类型（行为同冰冻/麻痹），苍白之火是独立 DoT（每跳同燃烧），三者只是类型不同、可与通用冰冻/麻痹/燃烧并存各算各的层数与时间
11. 全局参数（鲛人刷新等级等）不是 Buff，实现于游戏逻辑控制器/Config（策划案第二十章）；其中鲛人刷新等级按战斗剩余时间实时计算（`BattleManagerPassive.GetZombieSpawnLevel`：每经过 7.5% 战斗时间 +1，死亡漫步者被动再 +4，普通/精英共用）。受击类被动（防守方角色0 单次受伤上限、防守方角色1 致命拦截）不进 Buff 系统，在 `EntityData.OnDamaged` 的伤害管线里即时判定。等级上限分三档：角色等级上限 `save_max_level=10`（对应 `exp_character.csv` 的 1~10 级）、战斗期生成等级 `battle_max_level=20`（`EntityData.OnCreate` 钳制，最高刷出 20 级）、玩家等级上限取 `exp_player.csv` 的最大主键（`Config.player_max_level` 由表推断，不写死）
12. 表现：持续型 Buff 特效**只由客户端播**——`EntityPlayerManager.ApplyBuffVfx` 按 `SCEntityDisplayInfo.buffs` 查 `Config.buff_vfx` 增删跟随特效（映射表按《特效清单分别表1019》定稿），**技能 `PlayWave` 内禁止播 buff 特效**。下发时同类型多层**聚合成一条**：`level` = 各层等级之和，按协议 4bit 截断在 15（超出部分客户端不可见，服务端效果照常按真实层数算）；客户端只按 type 表现，层级差异不体现在特效与图标上。
13. 天使祝福（`AngelBless`）/ 恶魔祝福（`DemonBless`）：出伤 / 受伤乘区 Buff，与愈战愈勇同通道（`GetOutDamageMultiplier`：+25% / +100%；`GetInDamageMultiplier`：−25% / +25%），攻击方在伤害计算时读取；两者每跳分别回血 200 / 50，夹到生命上限。由 `NeutralStatueEntityData.OnKilled` 按雕像 value 挂给"把雕像血量清零那一次攻击的攻击者"；被 Buff/DoT（固定数值伤害）打死的没有归属。移速类单独一档，见"速度"节⑨
