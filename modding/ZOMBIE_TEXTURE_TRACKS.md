# 僵尸外部贴图替换部位与 Reanimation 轨道

本文依据 PC 中文版 `1.0.0.1051` 的反编译代码与 `Zombie*.reanim` 资源整理。只有真正包含图片帧的轨道才列入；`anim_walk`、`anim_eat` 等只负责帧区间的动画标签不属于可替换图片部位。

## 配置方式

```jsonc
"replacements": [
  { "scope": "body", "target": "head", "textureId": "KILL" },
  { "scope": "body", "track": "Zombie_tie", "textureId": "RAGE_TIE" },
  { "scope": "special", "track": "anim_idle", "textureId": "SPECIAL_HEAD" }
]
```

- `scope: "body"`：僵尸的 `mBodyReanimID`，绝大多数身体、头、手脚、防具和道具都在这里。
- `scope: "special"`：`mSpecialHeadReanimID` 指向的附加动画。旗帜僵尸在这里保存旗帜；豌豆头、机枪头、坚果头、高坚果头和辣椒头等特殊单位在这里保存附加头部动画。不同类型的轨道名并不相同，必须使用 `track`。
- `target`：普通 `Zombie.reanim` 的稳定语义别名。
- `track`：高级模式的原版轨道名，大小写不敏感。`target` 与 `track` 必须且只能写一个。
- 替换使用原版 `Reanimation::SetImageOverride`；图片会沿用该轨道的位移、旋转、缩放、显隐和绘制层级。轨道不存在时不会退回第 0 轨，也不会修改其他部位。

## 普通僵尸族：全部图片部位

普通、旗帜、路障、铁桶、铁门、鸭子救生圈，以及植物头僵尸的基础身体主要复用 `Zombie.reanim`。下表列出该动画族全部 30 个图片轨道。

| `target` | 原版轨道 | 部位/用途 |
| --- | --- | --- |
| `inner_arm_hand` | `anim_innerarm3` | 内侧手 |
| `inner_arm_lower` | `anim_innerarm2` | 内侧前臂 |
| `inner_arm_upper` | `anim_innerarm1` | 内侧上臂 |
| `flag_hand` | `Zombie_flaghand` | 旗帜握持挂点手 |
| `screen_door_inner_arm` | `Zombie_innerarm_screendoor` | 铁门后内侧手臂 |
| `neck` | `Zombie_neck` | 颈部 |
| `head` | `anim_head1` | 头部主体 |
| `inner_leg_upper` | `Zombie_innerleg_upper` | 内侧大腿 |
| `inner_leg_lower` | `Zombie_innerleg_lower` | 内侧小腿 |
| `inner_leg_foot` | `Zombie_innerleg_foot` | 内侧脚 |
| `outer_leg_upper` | `Zombie_outerleg_upper` | 外侧大腿 |
| `outer_leg_foot` | `Zombie_outerleg_foot` | 外侧脚 |
| `outer_leg_lower` | `Zombie_outerleg_lower` | 外侧小腿 |
| `body` | `Zombie_body` | 躯干主体 |
| `ducky_tube` | `Zombie_duckytube` | 鸭子救生圈 |
| `water_splash` | `Zombie_whitewater` | 普通水花 |
| `tie` | `Zombie_tie` | 领带 |
| `jaw` | `anim_head2` | 下颚 |
| `tongue` | `anim_tongue` | 舌头 |
| `mustache` | `Zombie_mustache` | 胡子装饰 |
| `screen_door` | `anim_screendoor` | 铁栅门主体 |
| `screen_door_inner_hand` | `Zombie_innerarm_screendoor_hand` | 铁门后内侧手 |
| `screen_door_outer_arm` | `Zombie_outerarm_screendoor` | 铁门外侧手臂 |
| `outer_arm_hand` | `Zombie_outerarm_hand` | 外侧手 |
| `outer_arm_upper` | `Zombie_outerarm_upper` | 外侧上臂 |
| `snorkel_water_splash` | `Zombie_whitewater2` | 潜水变体水花 |
| `outer_arm_lower` | `Zombie_outerarm_lower` | 外侧前臂 |
| `hair` | `anim_hair` | 头发 |
| `cone` | `anim_cone` | 路障帽 |
| `bucket` | `anim_bucket` | 铁桶帽 |

## 特殊僵尸主动画：全部图片轨道

下表按 Reanimation 动画族列出其全部图片轨道。使用这些名称时写 `scope: "body"` 和 `track`。列表存在表示引擎能覆盖该轨道，不表示同一张图片适合所有部件；每个特殊僵尸仍需独立检查尺寸、锚点、受伤阶段和掉落动作。

| 动画族 | 图片轨道 |
| --- | --- |
| `Zombie_backup` / `Zombie_dancer` | `Zombie_Jackson_innerarm_upper`, `Zombie_Jackson_innerarm_lower`, `Zombie_Jackson_innerarm_hand`, `Zombie_Jackson_innerleg_lower`, `Zombie_Jackson_innerleg_foot`, `Zombie_Jackson_innerleg_upper`, `Zombie_Jackson_body2`, `Zombie_Jackson_outerleg_foot`, `Zombie_Jackson_outerleg_lower`, `Zombie_Jackson_outerleg_toe`, `Zombie_Jackson_outerleg_upper`, `Zombie_dancer_belt`, `Zombie_Jackson_body1`, `anim_head1`, `anim_head2`, `anim_earing`, `anim_hair`, `Zombie_Jackson_colar`, `Zombie_Jackson_outerarm_upper`, `Zombie_outerarm_lower`, `Zombie_outerarm_hand` |
| `Zombie_balloon` | `Zombie_balloon_innerleg_lower`, `Zombie_balloon_innerleg_foot`, `Zombie_balloon_innerleg_upper`, `Zombie_innerarm_hand`, `Zombie_balloon_innerarm_upper`, `Zombie_balloon_innerarm_lower`, `Zombie_balloon_string`, `Zombie_balloon_bottom`, `Zombie_balloon_top`, `Zombie_balloon_body2`, `Zombie_balloon_body1`, `Zombie_balloon_outerleg_foot`, `Zombie_balloon_outerleg_lower`, `Zombie_balloon_outerleg_upper`, `anim_head1`, `anim_head2`, `Zombie_outerarm_hand`, `Zombie_outerarm_upper`, `Zombie_outerarm_lower`, `hat`, `balloon_pop`, `propeller` |
| `Zombie_bobsled` | `Zombie_dolphinrider_innerarm_lower`, `Zombie_dolphinrider_innerarm_hand`, `Zombie_dolphinrider_innerarm_upper`, `Zombie_dolphinrider_innerleg_lower`, `Zombie_dolphinrider_innerleg_foot`, `Zombie_dolphinrider_innerleg_upper`, `Zombie_dolphinrider_body2`, `Zombie_dolphinrider_body1`, `Zombie_dolphinrider_outerleg_foot2`, `Zombie_dolphinrider_outerleg_foot1`, `Zombie_dolphinrider_outerleg_lower`, `Zombie_dolphinrider_outerleg_upper`, `Zombie_outerarm_lowereating`, `anim_head1`, `anim_head2`, `Zombie_outerarm_handeating`, `Zombie_outerarm_lower`, `Zombie_outerarm_hand`, `Zombie_dolphinrider_outerarm_upper` |
| `Zombie_bungi` | `Zombie_bungi_body`, `Zombie_bungi_rightleg_upper`, `Zombie_bungi_rightleg_lower`, `Zombie_bungi_rightleg_foot`, `Zombie_bungi_leftleg_upper`, `Zombie_bungi_left_tatter`, `Zombie_bungi_leftshirt`, `Zombie_bungi_rightshirt`, `Zombie_bungi_leftleg_lower`, `Zombie_bungi_leftleg_tatter`, `Zombie_bungi_leftleg_foot`, `Zombie_bungi_rightarm_upper`, `Zombie_bungi_rightarm_lower`, `Zombie_bungi_rightarm_cuff`, `Zombie_bungi_rightarm_hand`, `Zombie_bungi_leftarm_upper`, `Zombie_bungi_leftarm_lower`, `Zombie_bungi_leftarm_hand`, `Zombie_bungi_body2`, `Zombie_bungi_rightarm_upper2`, `Zombie_bungi_leftarm_upper2`, `Zombie_bungi_jaw`, `anim_head1`, `Zombie_bungi_hair`, `Zombie_bungi_leftarm_hand2`, `Zombie_bungi_leftarm_lower2`, `Zombie_bungi_rightarm_hand2`, `Zombie_bungi_rightarm_lower2` |
| `Zombie_catapult` | `Zombie_catapult_crank`, `Zombie_catapult_driver_innerarm_hand`, `Zombie_catapult_driver_innerarm_lower`, `Zombie_catapult_driver_innerarm_upper`, `Zombie_catapult_crankarm`, `Zombie_catapult_driver_innerleg_foot`, `Zombie_catapult_driver_innerleg_lower`, `Zombie_catapult_driver_innerleg_upper`, `Zombie_catapult_driver_body`, `Zombie_catapult_driver_mouth`, `Zombie_catapult_driver_head`, `Zombie_catapult_driver_outerleg_foot`, `Zombie_catapult_driver_outerleg_lower`, `Zombie_catapult_driver_outerleg_upper`, `Zombie_catapult_driver_hankie`, `Zombie_catapult_driver_outerarm_hand`, `Zombie_catapult_driver_outerarm_upper`, `Zombie_catapult_driver_outerarm_lower`, `Zombie_catapult_innerwheel_rear1`, `Zombie_catapult_innerwheel_rear2`, `Zombie_catapult_body`, `Zombie_catapult_spring`, `Zombie_catapult_pole`, `Zombie_catapult_basket`, `Zombie_catapult_basketball`, `Zombie_catapult_basketball2`, `Zombie_catapult_basketball3`, `Zombie_catapult_basketball4`, `Zombie_catapult_basket_overlay`, `LawnMower_wheelpiece`, `Zombie_catapult_outerwheel_front1`, `Zombie_catapult_siding`, `Zombie_catapult_body_overlays`, `Zombie_catapult_body_overlays2`, `Zombie_catapult_engine`, `Zombie_catapult_manhole`, `Zombie_catapult_manhole_overlay`, `Zombie_catapult_tape` |
| `Zombie_digger` | `Zombie_digger_innerarm_lower`, `Zombie_digger_pickaxe`, `Zombie_digger_dirt`, `Zombie_digger_innerarm_hand`, `Zombie_digger_innerarm_upper`, `Zombie_digger_innerleg_foot`, `Zombie_digger_innerleg_lower`, `Zombie_digger_innerleg_upper`, `Zombie_digger_body`, `anim_head1`, `Zombie_digger_head_eye`, `anim_head2`, `Zombie_digger_outerleg_foot`, `Zombie_digger_outerleg_lower`, `Zombie_digger_outerleg_upper`, `Zombie_outerarm_hand`, `Zombie_outerarm_lower`, `Zombie_digger_outerarm_upper`, `Zombie_digger_rise`, `Zombie_digger_hardhat`, `Zombie_digger_dig` |
| `Zombie_disco` / `Zombie_Jackson` | `Zombie_Jackson_innerarm_upper`, `Zombie_Jackson_innerarm_lower`, `Zombie_Jackson_innerarm_hand`, `Zombie_Jackson_innerleg_lower`, `Zombie_Jackson_innerleg_foot`, `Zombie_Jackson_innerleg_toe`, `Zombie_Jackson_innerleg_upper`, `Zombie_Jackson_outerleg_lower`, `Zombie_Jackson_body2`, `Zombie_Jackson_outerleg_foot`, `Zombie_Jackson_outerleg_toe`, `Zombie_Jackson_outerleg_upper`, `Zombie_Jackson_body1`, `anim_head1`, `anim_head2`, `anim_hair`, `Zombie_Jackson_colar`, `Zombie_Jackson_outerarm_upper`, `Zombie_outerarm_lower`, `Zombie_outerarm_hand` |
| `Zombie_dolphinrider` | `Zombie_dolphinrider_innerarm_hand`, `Zombie_dolphinrider_innerarm_lower`, `Zombie_dolphinrider_innerarm_upper`, `Zombie_dolphinrider_innerleg_foot`, `Zombie_dolphinrider_innerleg_lower`, `Zombie_dolphinrider_innerleg_upper`, `Zombie_dolphinrider_dolphininwater`, `Layer 185`, `Layer 1852`, `Layer 1853`, `Layer 1854`, `Layer 1855`, `Zombie_dolphinrider_body2`, `Zombie_dolphinrider_body1`, `Zombie_dolphinrider_watershadow`, `Zombie_dolphinrider_outerleg_foot1`, `Zombie_dolphinrider_outerleg_foot2`, `Zombie_dolphinrider_outerleg_lower`, `Zombie_dolphinrider_outerleg_upper`, `Zombie_dolphinrider_whitewater`, `anim_head2`, `anim_head1`, `Zombie_dolphinrider_dolphinbody2`, `Zombie_dolphinrider_dolphinfin2`, `Zombie_dolphinrider_dolphinjaw`, `Zombie_dolphinrider_dolphinbody1`, `Zombie_dolphinrider_dolphinfin1`, `Zombie_outerarm_hand`, `Zombie_outerarm_lower`, `Zombie_dolphinrider_outerarm_upper` |
| `Zombie_football` | `zombie_football_leftarm_upper`, `zombie_football_leftleg_foot`, `zombie_football_leftleg_lower`, `zombie_football_leftleg_upper`, `zombie_football_lowerbody`, `zombie_football_rightleg_lower`, `zombie_football_rightleg_foot`, `zombie_football_rightleg_upper`, `zombie_football_upperbody2`, `zombie_football_leftarm_lower`, `zombie_football_upperbody`, `anim_hair`, `anim_head1`, `zombie_football_rightarm_upper`, `zombie_football_rightarm_lower`, `zombie_football_upperbody3`, `anim_head2`, `zombie_football_helmet`, `zombie_football_leftarm_hand`, `zombie_football_rightarm_hand` |
| `Zombie_gargantuar` | `Zombie_gargantuar_innerarm_upper`, `Zombie_gargantuar_innerarm_lower`, `Zombie_gargantuar_innerarm_hand`, `Zombie_gargantua_innerleg_foot`, `Zombie_gargantua_innerleg_lower`, `Zombie_gargantua_innerleg_upper`, `Zombie_gargantua_body2`, `Zombie_gargantuar_outerleg_foot`, `Zombie_gargantuar_outerleg_lower`, `Zombie_gargantuar_outerleg_upper`, `Zombie_gargantuar_trashcan2`, `Zombie_imp_innerarm_upper`, `Zombie_imp_innerleg_foot`, `Zombie_imp_innerleg_lower`, `Zombie_imp_innerleg_upper`, `Zombie_imp_outerleg_foot`, `Zombie_imp_outerleg_lower`, `Zombie_imp_outerleg_upper`, `Zombie_imp_body2`, `Zombie_imp_body1`, `Zombie_imp_head`, `Zombie_imp_jaw`, `Zombie_gargantuar_trashcan`, `Zombie_gargantua_body1`, `Zombie_gargantuar_whiterope`, `Zombie_imp_innerarm_lower`, `Zombie_imp_outerarm_upper`, `Zombie_imp_outerarm_lower`, `Zombie_gargantuar_rope`, `anim_head1`, `Zombie_gargantua_jaw`, `Zombie_gargantuar_telephonepole`, `Zombie_gargantuar_outerarm_upper`, `Zombie_gargantuar_outerarm_lower`, `Zombie_gargantuar_outerarm_hand`, `Zombie_gargantuar_innerarm_thumb` |
| `Zombie_imp` | `Zombie_imp_innerarm_upper`, `Zombie_imp_innerarm_lower`, `Zombie_imp_innerleg_foot`, `Zombie_imp_innerleg_lower`, `Zombie_imp_innerleg_upper`, `Zombie_imp_body2`, `Zombie_imp_body1`, `Zombie_imp_outerleg_foot`, `Zombie_imp_outerleg_lower`, `Zombie_imp_outerleg_upper`, `anim_head1`, `anim_head2`, `Zombie_imp_outerarm_upper`, `Zombie_outerarm_lower` |
| `Zombie_jackbox` | `zombie_jackbox_innerleg_foot`, `zombie_jackbox_innerleg_lower`, `zombie_jackbox_innerleg_upper`, `Zombie_jackbox_body2`, `zombie_jackbox_outerleg_foot`, `zombie_jackbox_outerleg_lower`, `zombie_jackbox_outerleg_upper`, `Zombie_jackbox_innerarm_upper`, `Zombie_jackbox_innerarm_lower`, `Zombie_jackbox_body1`, `anim_head1`, `anim_head2`, `Zombie_jackbox_handle`, `Zombie_jackbox_box`, `Zombie_jackbox_box2`, `Zombie_jackbox_clownneck3`, `Zombie_jackbox_clownneck2`, `Zombie_jackbox_clownneck1`, `Zombie_jackbox_clownhead`, `Zombie_jackbox_outerarm_upper`, `Zombie_jackbox_outerarm_lower` |
| `Zombie_ladder` | `Zombie_ladder_innererarm_hand`, `Zombie_ladder_innerleg_foot`, `Zombie_ladder_innerarm_hand`, `Zombie_ladder_innerarm_lower`, `Zombie_ladder_innerleg_upper`, `Zombie_ladder_innerleg_lower`, `Zombie_ladder_outerleg_foot`, `Zombie_ladder_outerleg_lower`, `Zombie_ladder_outerleg_upper`, `Zombie_ladder_body2`, `Zombie_ladder_hammer`, `Zombie_ladder_innerarm_upper`, `Zombie_ladder_body`, `anim_head2`, `anim_head1`, `Zombie_ladder_1`, `Zombie_outerarm_hand`, `Zombie_outerarm_lower`, `Zombie_ladder_outerarm_upper`, `Zombie_ladder_innerarm_hand2` |
| `Zombie_paper` | `Zombie_paper_rightarm_upper`, `Zombie_paper_rightarm_lower`, `Zombie_paper_hands2`, `Zombie_paper_lowerbody2`, `Zombie_paper_rightleg_upper`, `Zombie_paper_rightleg_lower`, `Zombie_paper_rightfoot`, `Zombie_paper_leftleg_upper`, `Zombie_paper_leftleg_lower`, `Zombie_paper_leftfoot`, `Zombie_paper_lowerbody1`, `Zombie_paper_body`, `Zombie_paper_leftarm_upper`, `Zombie_paper_leftarm_lower`, `anim_hair`, `anim_head1`, `anim_head_look`, `anim_head_pupils`, `anim_hairpiece`, `anim_head_jaw`, `anim_head_glasses`, `Zombie_paper_paper`, `Zombie_paper_hands` |
| `Zombie_pogo` | `anim_innerarm3`, `anim_innerarm2`, `anim_innerarm1`, `Zombie_innerleg_lower`, `Zombie_innerleg_upper`, `Zombie_innerleg_foot`, `Zombie_pogo_stick3`, `Zombie_pogo_stick2`, `Zombie_outerleg_lower`, `Zombie_outerleg_upper`, `Zombie_outerleg_foot`, `Zombie_body`, `anim_head1`, `anim_head2`, `anim_head_glasses`, `anim_hair`, `Zombie_outerarm_hand`, `Zombie_outerarm_upper`, `Zombie_outerarm_lower`, `Zombie_pogo_stick`, `Zombie_pogo_stickhands` |
| `Zombie_polevaulter` | `Zombie_polevaulter_pole2`, `Zombie_polevaulter_pole`, `Zombie_polevaulter_innerhand`, `Zombie_polevaulter_innerleg_upper`, `Zombie_polevaulter_innerleg_foot`, `Zombie_polevaulter_innerleg_toe`, `Zombie_polevaulter_innerleg_lower`, `Zombie_polevaulter_outerleg_upper`, `Zombie_polevaulter_outerleg_foot`, `Zombie_polevaulter_outerleg_toe`, `Zombie_polevaulter_outerleg_lower`, `Zombie_polevaulter_innerarm_upper`, `Zombie_polevaulter_innerarm_lower`, `Zombie_polevaulter_body2`, `Zombie_polevaulter_body1`, `anim_head1`, `anim_head2`, `anim_hair`, `Zombie_outerarm_hand`, `Zombie_polevaulter_outerarm_upper`, `Zombie_polevaulter_outerarm_lower` |
| `Zombie_snorkle` | `Zombie_snorkle_innerarm_hand`, `Zombie_snorkle_innerarm_lower`, `Zombie_snorkle_innerarm_upper`, `Zombie_snorkle_innerleg_upper`, `Zombie_snorkle_innerleg_foot`, `Zombie_snorkle_innerleg_lower`, `Zombie_snorkle_outerleg_lower`, `Zombie_snorkle_outerleg_upper`, `Zombie_snorkle_outerleg_toe`, `Zombie_snorkle_body2`, `Zombie_snorkle_body1`, `anim_head1`, `anim_head_lips`, `anim_head_jaw`, `anim_head_snorkle`, `Zombie_outerarm_hand`, `Zombie_snorkle_outerarm_upper`, `Zombie_outerarm_lower`, `Zombie_snorkle_whitewater2`, `Zombie_snorkle_whitewater` |
| `Zombie_yeti` | `Zombie_yeti_innerarm_hand`, `Zombie_yeti_innerarm_upper`, `Zombie_yeti_innerarm_lower`, `Zombie_yeti_innerleg_foot`, `Zombie_yeti_innerleg_lower`, `Zombie_yeti_innerleg_upper`, `Zombie_yeti_body`, `Zombie_yeti_outerleg_foot`, `Zombie_yeti_outerleg_lower`, `Zombie_yeti_outerleg_upper`, `anim_head1`, `anim_head2`, `Zombie_outerarm_hand`, `Zombie_yeti_outerarm_upper`, `Zombie_outerarm_lower` |
| `Zombie_zamboni` | `Zombie_zambonidriver_lowerarm_inner`, `Zombie_zamboni_4`, `Zombie_zamboni_3`, `Zombie_zamboni_seat`, `Zombie_zambonidriver_legs`, `Zombie_zambonidriver_innerarm_hand`, `Zombie_zambonidriver_body`, `Zombie_zambonidriver_upperarm`, `Zombie_zambonidriver_lowerarm_outer`, `Zombie_zambonidriver_outerarm_hand`, `Zombie_zambonidriver_shoulder`, `Zombie_head`, `Zombie_jaw`, `Zombie_zambonidriver_mullet2`, `Zombie_zambonidriver_beanie`, `Zombie_zamboni_wires`, `Zombie_zamboni_brush`, `Zombie_zamboni_wheel1`, `Zombie_zamboni_wheel2`, `Zombie_zamboni_2`, `Zombie_zamboni_1` |

## Boss 与辅助动画

| 动画族 | 图片轨道 |
| --- | --- |
| `Zombie_boss` | `Boss_innerarm_bits`, `Boss_innerarm_lower`, `Boss_innerarm_finger4`, `Boss_innerarm_finger2`, `Boss_innerarm_finger1`, `Boss_innerarm_finger3`, `Boss_innerarm_hand`, `Boss_RV_wheel1`, `Boss_RV_wheel2`, `Boss_RV`, `Boss_innerarm_thumb2`, `Boss_innerarm_thumb1`, `Boss_innerarm_upper`, `Boss_innerleg_foot`, `Boss_innerleg_bits`, `Boss_innerleg_upper`, `Boss_innerleg_lower`, `Boss_body2`, `Boss_outerleg_foot`, `Boss_outerleg_bits`, `Boss_outerleg_upper`, `Boss_outerleg_lower`, `boss_body1`, `Boss_neck`, `Boss_innerjaw`, `Boss_mouthglow`, `Boss_mouthglow_red`, `Boss_jaw`, `Boss_eyeglow`, `Boss_eyeglow_red`, `Boss_head2`, `Boss_eyeglow_black`, `boss_antenna2`, `Boss_head`, `boss_antenna`, `Boss_outerarm_thumb2`, `Boss_outerarm_hand`, `Boss-outerarm_finger4`, `Boss-outerarm_knuckle4`, `Boss-outerarm_finger3`, `Boss-outerarm_knuckle3`, `Boss-outerarm_finger2`, `Boss-outerarm_knuckle2`, `Boss-outerarm_finger1`, `Boss-outerarm_knuckle1`, `Boss_outerarm_thumb1`, `Boss_outerarm_bits`, `Boss_outerarm_lower`, `Boss_outerarm_upper` |
| `Zombie_Boss_driver` | `flagpole`, `flag`, `Driver_innerarm_upper`, `Driver_innerarm_lower2`, `Driver_innerarm_hand`, `Driver_innerrarm_lower`, `Driver_body`, `Driver_brain`, `Driver_face`, `Driver_jaw`, `Driver_outerarm_upper`, `Driver_outerarm_lower2`, `Driver_outerarm_hand`, `Driver_outerarm_lower` |
| `Zombie_boss_fireball` | `Layer 47`, `fireball`, `fireball_chunks`, `multiply`, `additive`, `superglow` |
| `Zombie_boss_iceball` | `Layer 64`, `iceball`, `ice_crystal1`, `ice_crystal1_2`, `ice_crystal2`, `ice_crystal3`, `ice_overlay`, `ice_multiply`, `ice_highlight` |
| `Zombie_flagpole` | `Zombie_flagpole`, `Zombie_flag` |
| `Zombie_hand` | `arm`, `hand`, `finger1-2`, `finger1-1`, `finger2-2`, `finger2-1`, `finger3-2`, `finger3-1`, `finger4-2`, `finger4-1`, `rock2`, `rock5`, `rock2.2`, `rock1`, `rock3`, `rock6`, `rock5.2`, `rock4`, `rock6.2`, `rock4.2`, `rock5.3`, `rock5.4`, `rock5.5` |
| `Zombie_surprise` | `Layer 1` |
| `ZombiesWon` | `ZombiesWon` |

## 明确限制

- 当前替换入口面向存活僵尸的 `body` 与 `special` Reanimation。Boss 火球、冰球、气球螺旋桨等没有保存在这两个 ID 中的独立子动画，虽然轨道目录已列出，但还需要新的作用域解析器后才能从精英 JSON 直接替换。
- 同一外部图片替换多个分段手脚轨道会在每段各绘制一次，并分别继承每段变换；这不等同于一张完整角色皮肤。完整皮肤应按轨道拆图。
- 原版受伤逻辑会在部分轨道上更换损坏图片。运行时必须在绘制前重新应用精英覆盖，才能保证配置图片不会被伤害阶段覆盖。
- PNG 的透明区域、画布尺寸和原版素材锚点会直接影响结果。64×64 图片可以用于验证头部轨道，但正式素材应按被替换原图的画布和锚点制作。
