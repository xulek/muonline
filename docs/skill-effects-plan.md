# Plan: skill effects vs SourceMain5.2

Procedure for every item: `.claude/skills/mu-skill-effects` (trace A–H → port sheet → `Joints/` toolkit →
build DX + GL → `graphify update .` → in-game check). One skill at a time.

Legend: **R** = redo (C# effect exists, not verified against the source), **N** = new (no factory),
**✓** = done and verified. Master variants (`_UP`, `+1…+4`) go with their base skill.

## 0. Toolkit (before the first skill that needs it)

1. `SourceQuadBatch.AddTrail`: `m_byReverseUV` 1 / 2 / 3 — ✓ (`reverseUV` argument; thunder + bTileMapping U via denominator/scroll + `faceTwoUOffset`).
2. `SourceThunderPool` (JOINT_THUNDER with fixed target) — ✓. `SourceModelRenderer.DrawInstance` rebuilds dirty buffers (models were invisible without Update) — ✓. `SourceStonePool` — ✓ (older ports still have inline stones). Sprite-sheet helper — ✓ `SourceQuadBatch.AddSpriteRegion`. Shared spark pool — ✓ `SourceSparkPool`. Camera `EarthQuake` — ✓ `Camera.SetEarthQuake` (pitch offset in degrees, ×0.2 per frame; wired into Rageful Blow, Strike of Destruction, Hellfire, Earthshake, Dragon Roar).
3. Move `RagefulBlowEffect` / `DarkHorseEarthquakeEffect` off mirrored `MathUtils.AngleMatrix` (done with their skills).
4. Self skill on right click anywhere — ✓. Elf buffs (26 / 27 / 28 / 234) on a hovered player, self otherwise — ✓ `GameSceneSkillController` (AttackElf, ZzzInterface.cpp:4856-5005; walks into range first; the party-list window selection and master ids are not handled).
5. Weapon blur (`CreateWeaponBlur` / `SourceBlur`) — ✓ players incl. RF sword-form gloves (`MoveBlurEffect`, BlurMapping 5); ✓ Mega / Alpha Crust, Omega Wing (BlurMapping 6) via `Monsters/MonsterWeaponBlur` (heuristic `WeaponTrailEffect` removed from `WeaponObject`). Other monsters correctly have no blur: the sword branch and the Aegis / Death Centurion / Shriker branches only create blurs when Weapon[].Type != -1, which monsters never set (ZzzCharacter.cpp:11556, 3838). N: per-map `MoveBlurEffect` functions.

## 1. Dark Knight

| # | Skill | Id | State |
|---|---|---|---|
| 1 | Swell Life | 48 | ✓ |
| 2 | Falling Slash, Lunge, Uppercut, Cyclone, Slash | 19–23 | ✓ weapon blur port + actions (in-game check pending) |
| 3 | Defense | 18 | ✓ action only (no effect in original) |
| 4 | Twisting Slash | 41 | ✓ gate AnimationFrame>=5, weapon model opaque pass fixed (in-game check pending; Hellas water branch, terrain light, riding actions not ported) |
| 5 | Rageful Blow | 42 | ✓ `FuryStrikeEffect` (in-game check pending; terrain lights, Hellas branch not ported; old `RagefulBlowEffect` removed) |
| 6 | Death Stab | 43 | ✓ (in-game check pending) |
| 7 | Combo | 59 | ✓ (in-game check pending; death knockback not ported) |
| 8 | Impale | 47 | ✓ (in-game check pending; Fenrir action not mapped) |
| 9 | Fire Breath | 49 | ✓ (in-game check pending; fly action variant, terrain light not ported) |
| 10 | Strike of Destruction | 232 | ✓ (in-game check pending; MODEL_STONE sub 13 invisible in original) |

## 2. Dark Wizard

| # | Skill | Id | State |
|---|---|---|---|
| 1 | Energy Ball, Fire Ball, Lightning, Ice, Poison | 17, 4, 3, 7, 1 | ✓ new Joints ports (in-game check pending; AddTerrainLight not ported; old ScrollOf* kept for scrolls) |
| 2 | Meteorite, Power Wave, Flame | 2, 11, 5 | ✓ (FireBallEffect meteor variant, PowerWaveEffect, FlameEffect; in-game check pending) |
| 3 | Twister, Evil Spirit, Hellfire, Inferno | 8, 9, 10, 14 | ✓ (+ Hellfire bone fires during PLAYER_SKILL_HELL) TwisterEffect, EvilSpiritEffect, HellFireEffect, InfernoEffect (in-game check pending; terrain lights, Hellas branches not ported; Twister / Evil Spirit / Inferno have no EarthQuake in the source, Hellfire has it) |
| 4 | Aqua Beam, Cometfall | 12, 13 | ✓ AquaBeamEffect, CometfallEffect (in-game check pending) |
| 5 | Teleport, Teleport Ally | 6, 15 | ✓ TeleportSparkEffect on Joints, SOUND_MAGIC (in-game check pending) |
| 6 | Soul Barrier (cast + bubble) | 16 | ✓ SpearSkillOrbitEffect sub 0 re-checked against ZzzEffectJoint.cpp:1498 / 4392 / 7081 (MaxTails 30 / F, Light 1, darkening above owner + 50, mid-tail sprite); new `SoulBarrierSkillEffect`: release at AnimationFrame >= 5 re-creates the joints with scale 20 and replays SOUND_SOULBARRIER (ZzzCharacter.cpp:4835-4855; buff packet alone keeps scale 50). Self/party targeting, shield lifetime follows server status (in-game check pending; MODEL_DARK_PHEONIX_SHIELD caster branch not ported) |
| 7 | Decay, Ice Storm (+ Ice Up 302–306) | 38, 39 | ✓ DecayEffect, IceStormEffect; Flame area path (0x1E, sub 0) added (in-game check pending) |
| 8 | Nova (charge 58 + release 40) | 58, 40 | ✓ NovaChargeEffect / NovaReleaseEffect (charge emits per frame like the original; pools capped) |
| 9 | Expansion of Wizardry | 233 | ✓ moved to Joints, 2LINE_GHOST joints added (in-game check pending; ring brightness truncates like the original short PKKey) |
| 10 | Gigantic Storm | 237 | ✓ re-ported (thunder joints sub 16, MAGIC+1 / CHROME_ENERGY2 decals, smoke 54; invented light and bolt pool removed) |

## 3. Fairy Elf

| # | Skill | Id | State |
|---|---|---|---|
| 1 | Triple Shot / arrows | 24 | ✓ `ArrowProjectileEffect` rewritten on Joints from CreateArrow / CreateArrows: 19 MODEL_ARROW* types (init / move / render), CheckClientArrow (any character within 100 stops a non-piercing arrow), trails (FLARE+1 sub 12..15 / 18, JOINT_ENERGY 5), holy FLARE sub 11 orbit, particles (FIRE 0, FLOWER, BUBBLE 1, FLARE 5, SMOKE 0 / 13 / 24 / 34 / 35, SPARK+1 10 / 30, SHINY 3), MODEL_WAVES 3 / 4, MODEL_FEATHER 0 / 1, CreateBomb; release at AnimationFrame >= 5. In-game check pending. AddTerrainLight via new toolkit `SourceTerrainLights` (reusable DynamicLight per arrow, range 2 tiles). Great Reign sparks (JOINT_SPARK sub 3 added to `SourceSparkPool`) and the Drill bone BITMAP_SPARK particle ported. Sylph Wind arrow gets the RENDER_CHROME5 bright pass (`SourceModelRenderer.ChromePass5`, drawn as an overlay on top of the base pass, not as the original single chrome-UV pass). Not ported: BITMAP_EXPLOTION sub 1 of FLARE sub 25, siege no-attack zone, hotkey-dependent PIERCING branch of Kind 0 arrows; BITMAP_BUBBLE texture (Object8/drop01.jpg) is map-specific as in the original |
| 2 | Heal, Greater Defense, Greater Damage | 26, 27, 28 | ✓ HealingCircleEffect on Joints (gate on caster frame 5); buff visuals: SpearSkillOrbitEffect sub 4 (Defense/Attack, shared), AttackBuffSparkEffect; invented auras replaced; Poison/Ice body tint (StatusBodyLight) |
| 3 | Summons (Goblin … Soldier) | 30–36 | ✓ BITMAP_MAGIC+1 sub 3 on the caster (HealingCircleEffect) |
| 4 | Penetration, Ice Arrow | 52, 51 | ✓ Penetration gathering (BITMAP_GATHERING, SOUND_PIERCING at AttackTime 3); arrows SubType 2 with MODEL_PIERCING + 4 FLARE+1 spiral joints, SOUND_FLASH / SOUND_BOW01, flying through targets (Kind 1); two arrows per targeted cast as the release switch calls CreateArrows twice; Deep Impact (46): MODEL_ARROW_IMPACT + FLASH sub 4 trail, FLASH sub 2 / 3 strike (BITMAP_SHOCK_WAVE sub 4 landing decal not ported). In-game check pending |
| 5 | Infinity Arrow | 77 | ✓ Joints port incl. carrier FLARE+1 sub 16 flares (bone-29 F bug: 25 FPS result used) |
| 6 | Recovery | 234 | ✓ Joints port incl. remote-caster BITMAP_IMPACT, FLARE sub 3 embers (LIGHT sub 5) |
| 7 | Multi-Shot | 235 | ✓ ring set (`SourceMultiShotPool`) + MODEL_BLADE_SKILL sub 1 timing: 6 arrows on even ticks at yaw ±7 / ±21 / ±35 with SHINY+1 sprites (25 Hz behaviour: one arrow per tick). In-game check pending; BITMAP_SPARK+1 joint sub 1 and the MODEL_BLADE_SKILL model itself not ported |

## 4. Magic Gladiator

| # | Skill | Id | State |
|---|---|---|---|
| 1 | Power Slash | 56 | ✓ re-ported (smoke 3/11 exact, per-frame sprite rotation, SWORD3 at release) |
| 2 | Fire Slash | 55 | ✓ children exact (SourceThunderPool sub 3, BITMAP_FIRE 0, SPARK+1 2) |
| 3 | Spiral Slash | 57 | ✓ re-traced `BITMAP_FLARE` sub 23, fractional frame / AttackTime gates and repeated low-window spawns; targeted cast and ONE_FLASH action fixed (in-game check pending; effect loading can delay its first tick; joint pool is shared by these casts but not by other effect types) |
| 4 | Flame Strike | 236 | ◐ source-traced: area packet at caster tile, immediate release sound, model scale/alpha, mesh-1 UV animation and three object blurs (in-game comparison pending; effects begin after asset loading; verify bone transform and model lighting against the original) |

## 5. Dark Lord

| # | Skill | Id | State |
|---|---|---|---|
| 1 | Force / Force Wave | 60, 66 | ✓ `DarkLordForceEffect` (Z offsets × F, PIERCING2 move order, MODEL_WAVES sub 2 Z, beams per tick, BodyLight, model animation, release gate 14 ticks, SOUND_SKILL_SWORD1 at packet, strike action) |
| 2 | Fire Burst (+ 508, 514) | 61 | ✓ `FireBurstEffect` rewritten on Joints (3 homing PIER_PART heads, segments + BITMAP_FIRE+1, DARKLORD_SKILL flashes, single-channel SOUND_ATTACK_FIRE_BUST_EXP; target H = BoundingBoxLocal.Max.Z) |
| 3 | Earthshake (+ 515–519) | 62 | ✓ `DarkHorseEarthquakeEffect` (RenderDarkHorseSkill: shock wave decals, ground stones + smoke 11 + stone sub 10, FuryStrikeEffect kind 2; terrain light not ported) |
| 4 | Summon (party) | 63 | ✓ `PartySummonEffect` (MAGIC_EMBLEM texture, BodyLights, FLARE_BLUE particle + joint 19, gate 5 ticks, PLAYER_ATTACK_TELEPORT family; packet path now uses GetPacketSkillAnimation for DL mount-dependent actions) |
| 5 | Increase Critical Damage | 64 | ✓ `DarkLordCriticalDamageEffect` (normal blend, Velocity 0.1 animation, weapon link bones, AF 5 gate, PLAYER_SKILL_HAND1 family instead of 71) |
| 6 | Electric Spike | 65 | ✓ `ElectricSpikeEffect` (Z 150F, MaxTails / F, AF 5.5 gate, ride/Fenrir height, SOUND_ELEC_STRIKE at release, PLAYER_SKILL_FLASH instead of 71) |
| 7 | Fire Scream | 78 | ✓ `FireScreamEffect` rewritten on Joints (3 DARK_SCREAM + FIRE pairs, JOINT_FORCE 7, BLUE_BLUR 1, FLAME 8, AF 5 gate) |
| 8 | Chaotic Diseier | 238 | ✓ `ChaoticDiseierEffect` (ghost joints / F tails + smoke 59, MODEL_DESAIR + MODEL_FEATHER minus models, SHINY+6 sprites per frame, CreateBomb for the hero target) |
| 9 | Explosion | 79 | ✓ `DarkLordExplosionEffect` (3 x CreateBomb at the target via new `SourceBombPool`) |

## 6. Common / MG-DL support

| # | Skill | Id | State |
|---|---|---|---|
| 1 | Stun / Cancel Stun | 67, 68 | ✓ `StunCastEffect` / `FlashStrikeEffect` on Joints (FLASH 7→6 with axis-Y tails + reverse UV 3, EXPLOTION+1, JOINT_THUNDER sub 4, ADV_SMOKE+1 GL_ADD; FLASH 0/1 strike; BMS sounds only in Battle Castle; mount action family 67–72; MODEL_SKIN_SHELL impact silhouette intentionally not ported: RenderMeshEffect spawns the shells at `VertexTransform` of the Shadow01 body, which BMD::Transform leaves in model space when called with Translate = false (ZzzBMD.cpp:242, ZzzBMD.cpp:1870, called from ZzzEffectJoint.cpp:6663), i.e. near the map origin and not at the target, so the original shows nothing on the target) |
| 2 | Swell Mana | 69 | ✓ `SwellManaEffect` on Joints (MANA_RUNE 0/1 with chrome bright pass, 36 JOINT_SPIRIT 21 + LIGHT sprites, MAGIC+1 sub 10 decals, 14-tick release) |
| 3 | Invisibility / Cancel | 70, 71 | ◐ / ◐: cast particles, rings, cloak status, equipment/elf-buff suppression and targeted cancellation ported; MODEL_SPEARSKILL sub 9 (elf-buff orbit of a Fenrir rider outside the safe zone: +140 height, darkening above +180) ✓ in `SpearSkillOrbitEffect`. Remaining: siege enemy visibility (no siege system), cancellation impact silhouette (see Stun / Cancel Stun: the original spawns it in model space near the map origin, so it is not drawn), in-game verification |
| 4 | Abolish Magic | 72 | ◐ source-traced six joints, terrain shock waves, targeted input and buff removal (in-game check pending; Hellas water branch and globally shared pools not ported) |
| 5 | Plasma Storm (Fenrir) | 76 | ◐ Joints port, area target, caster-tile request, Fenrir action and colour; in-game check pending; PvP secondary targets, terrain light, shared pool and original pool-slot initial scale remain |
| 6 | Lightning Shock | 230, 456 | ◐ Joints cast/impact and effect 85 target particles/cracks ported; base and Strength use caster-tile area request and original animation. In-game verification and original movement request remain; effect-local pools/RNG, asynchronous load timing and original effect-slot child scheduling differ |

## 7. Summoner

| # | Skill | Id | State |
|---|---|---|---|
| 1 | Drain Life | 214, 458 | ◐ targeted area request, local target correlation, mount actions and Joints effect ported; remote 0x1E contains no target ID; terrain lights, globally shared pools, original stale start-tail scale and in-game verification remain |
| 2 | Chain Lightning | 215 | ✓ `ChainLightningEffect` from the new 0xBF 0x0A handler (ReceiveChainMagic: mount action, hops sub 0 hands 37/28 → target, sub 1/2 previous target +80 → next, BITMAP_LIGHT sub 5 bone flashes at LifeTime 15); `ChainLightningCastEffect`: hand MODEL_FENRIR_THUNDER sub 2 + BITMAP_LIGHT while PLAYER_SKILL_CHAIN_LIGHTNING (ZzzCharacter.cpp:10545), release sound after 14 ticks; target required for the request; new shared `SourceHomingThunderPool` (JOINT_THUNDER sub 0/28 + ENERGY/SMOKE children, also used by Lightning). In-game check pending; terrain lights, sub 1/2 uninitialised vLight (sub 0 colour used) not ported |
| 3 | Lightning Orb | 216 | ✓ `LightningOrbEffect` on Joints (straight flight Direction (0,-60,0), CheckTargetRange 100 → FpsChecked burst sub 1, all sprites / MAGIC 0 / SPARK+1 13, 20 / SHOCK_WAVE 0 / SMOKE 40 particles, MODEL_FENRIR_THUNDER sub 2 forearm (bone 27) during the action and sub 3 at the burst via new `SourceFenrirThunderPool`; release after 14 ticks, only with a target). OpenMU has no skill 216: untestable in game; orb Visible test uses C# Visible instead of frustum visibility |
| 4 | Explosion / Requiem / Pollution (book summons) | 223, 224, 225 | ✓ `SummonBookEffectBase` (casting circle: BITMAP_MAGIC 9/10 decals, Suhwanzin models, tile-corner target, book level → SubType) + `SahamuttEffect` (charge, CreateBomb3 via new `SourceBomb3Pool`, bone fires), `NeilEffect` (knives, grounds, red bone sprites, object blur), `LagulEffect` (controller smoke/cloud/water, JOINT_SPIRIT 24, model sub 1, JOINT_ENERGY 48-53 bone trails); target DOT visuals `SummonDotEffect` on statuses 74 / 75 (EQUIP_HEAD sub 1: FIRE_CURSEDLICH bone fires / LIGHT+2 sub 5). In-game check pending. Not ported: Sahamutt RENDER_CHROME7 pass, SetAction blending, Bomb3 stones sub 14, terrain lights |
| 5 | Sleep, Blind, Weakness, Innovation | 219, 220, 221, 222 | ✓ `AliceSkillEffectBase` (bone 37 hand sprites + LIGHT+2 sub 0/4 + CLUD64 sub 3/5 while PLAYER_SKILL_SLEEP.., release after 14 ticks), `AliceBuffEffect` (MAGIC+1 sub 11/12 decal, elshildring rings sub 0/1/2 with sprites + JOINT_HEALING 15/16, Blind minus / RENDER_DARK), `SummonerCurseEffect` (MAGIC_ZIN x5, Suhwanzin2/22/222 sub 1, SHINY+6 / PIN_LIGHT emitters, sound at release); status visuals `AliceDebuffEffect` (72 Sleep water, 73 Blind LIGHT+2 sub 6, 76 / 77 shiny + pin on bones); actions fixed (217 / 218 / 220 → PLAYER_SKILL_SLEEP, 219 / 221 / 222 mount variants), 220 request type. Old `SummonerFxEffect` toolkit removed. In-game check pending. Hand colours follow the cast skill (original: local hero's selected skill); 221 / 222 request type left as is (original sends MagicContinue with own tile) |
| 6 | Damage Reflection, Berserker | 217, 218 | ✓ cast via `AliceBuffEffect` (rings sub 2 on the target / sub 0 on the caster, Berserker release sound); status visuals `ThornsBuffEffect` (71: BITMAP_FLARE sub 43 emitter → sub 44 rising ribbons + PIN_LIGHT sprites) and `BerserkerBuffEffect` (81: ORORA sub 0..3 hand particles, SPARK+2 sub 2/3, LIGHT_MARKS on 14 bones). In-game check pending. ORORA replacement uses the 25 Hz behaviour (FpsChecked chain break at high FPS not reproduced); terrain lights n/a |
| 7 | Spells / immunities / potions | 200–212 | — skipped after Step A: in 5.2 these ids are not Summoner skills (200–202 Kundun monster skills in GMHellas.cpp, 205–209 Halloween item events, 210–212 Illusion Temple event skills driven by NewUICursedTempleSystem.cpp through its own packet); the C# client has no Illusion Temple system |
| 8 | Shield-Burn | 213 | — skipped after Step A: AT_SKILL_CURSED_TEMPLE_SUBLIMATION, Illusion Temple event skill (CreateCursedTempleSkillEffect, NewUICursedTempleSystem.cpp:1293), no event system in C# |

## 8. Rage Fighter

| # | Skill | Id | State |
|---|---|---|---|
| 1 | Killing Blow | 260 | ✓ `KillingBlowEffect` moved to Joints (wolf head sub 0 at bone 27 with Scale 1.5, jittered-angle PIN_LIGHT joints + flying sub 1 / 2 copies, SBUMB strip particles at the live target, attack sound at 15 / 9 / 6; invented fades removed: the model is additive without BlendMesh). In-game check pending; `Owner->Velocity +=` side effect not ported |
| 2 | Beast Uppercut | 261 | ✓ `BeastUppercutEffect` moved to Joints (DUMMY_R → DUMMY_L models at Scale 0.9 with Velocity 2 × PlaySpeed, bombs at frame 3 / 5, FORCEPILLAR joints + FLARE sprite on dummy bone 0, CreateBomb3 sub 3 incl. new MODEL_STONE sub 14 debris; hero gate AnimationFrame 2; cast sound moved to the effect). In-game check pending; DUMMY_L uses the 25 Hz behaviour (FpsChecked creation after the parent is dead) |
| 3 | Chain Drive | 262 | ✓ `ChainDriveEffect` moved to Joints (SWORDEFF ribbon joints on bone 5, SHOCKWAVE01 / 02 / SPIN01 with the per-frame decays and absolute Z, WOLF_HEAD_EFFECT2 sub 3 / 5 + flying 4 / 6, DAMAGE1 and SHINY+4 target particles, hits at owner frame 6 / 11). In-game check pending; one-shot FpsChecked spawns use the 25 Hz behaviour |
| 4 | Dark Side | 263 | ◐ `DarkSideEffect` moved to Joints: cast part exact (EVENT_CLOUD sub 1 and MAGIC sub 14 terrain decals, WINDFOCE sub 2 + two sub 3 with BodyLight × 6), strike = SHOCKWAVE01 sub 3 → 5 + DAMAGE2 particle on the packet target. Blocked: the 0x4B RageAttackRange target list (5 targets) does not exist in MUnique.OpenMU.Network.Packets 0.9.8 (the server never sends it), so only the packet target is struck; DarksideRendering ghost body afterimages / dummies (+ SHOCKWAVE01 sub 4) need a character afterimage renderer (re-rendering every body part at another pose / position), which the client does not have (same gap as RenderCharacter_AfterImage of Dragon Slasher). In-game check pending |
| 5 | Dragon Roar | 264 | ✓ `DragonRoarEffect` + `DragonRoarHitEffect` moved to Joints (LAVA joints on bones 36 / 28 sampled at lagged frames, SHOCKWAVE01 sub 1, GROUND01 sub 1 / 2, HIK fire column via new `SourceHikFirePool`, SPARK sub 12, BLOW_OF_DESTRUCTION sub 2 → DRAGON_LOWER_DUMMY with V scroll + LIGHT_RED sub 4 decal; hits: VOLCANO_OF_MONK sub 1..3 with core pass, VOLCANO_STONE, TARGETMON fires, LIGHT_RED sub 3). In-game check pending; AddTerrainLight not ported |
| 6 | Dragon Slasher | 265 | ✓ `DragonSlasherEffect` moved to Joints (DRAGON_KICK_DUMMY logic, GROUND_WIND joints on dummy bone 2 with frame lag, ENERGY 7 / LIGHTNING_MEGA1 / SPARK+1 3, 8 / SPARK 13 target bursts, multishot rings via new `SourceMultiShotPool`, attack sound at frame 8). In-game check pending; RenderCharacter_AfterImage not ported |
| 7 | Ignore Defense, Increase Health, Increase Block | 266, 267, 268 | ✓ `RageBuffEffect` moved to Joints (SWORD_EFFECT_MONO pillars + SHOCKWAVE01 sub 2, 3 × GROUND01 sub 0, 3 × EVENT_CLOUD, WINDFOCE sub 0 / 4 / 5 + sub 3, MAGIC sub 13); new `RageBuffAuraEffect` on statuses 129 / 130 / 131 (LIGHT_RED sub 0 decal + bone sprites, SMOKELINE1 sub 5 fists, WINDFOCE sub 1 + WINDFOCE_MIRROR). In-game check pending; caster body passes (RageFighterEffect) not ported |
| 8 | Charge | 269 | ✓ `SwordForceRushEffect` on Joints, shared with Crescent Moon Slash (44, AT_SKILL_RUSH): charge sparks + FIRE sub 18 (ground_smoke.tga, alpha blend) / sub 2, SWORD_FORCE sub 2 / 0 with afterimages sub 3 / 1 (knightSt_blue texture for 269), release at AnimationFrame > 5, SOUND_BCS_RUSH; multishot rings for the hero cast. In-game check pending; AddTerrainLight not ported |
| 9 | Phoenix Shot | 270 | — skipped after Step B: 5.2 has no cast path (no case in UseSkillRagefighter / SetRageSkillAni / ReceiveMagic; RageCreateEffect(AT_SKILL_PHOENIX_SHOT) is never called and MODEL_PHOENIX_SHOT has an empty init and no move / render case) |

## 9. Ids with unclear status (Step A first: skip if not in 5.2 `_enum.h`)

| # | Skill | Id | State |
|---|---|---|---|
| 1 | Lance | 45 | ✓ `LanceEffect` (AT_SKILL_JAVELIN: 3 × MODEL_SKILL_JAVELIN homing with MoveHumming, POUNDING_BALL sub 1 trail, chrome pass without mesh 1, SOUND_BCS_JAVELIN; gate AnimationFrame 5). In-game check pending; AddTerrainLight not ported |
| 2 | Mana Rays | 73 | ✓ `ForceJointSkillEffect` (AT_SKILL_DEATH_CANNON: JOINT_FORCE sub 4 with BITMAP_HOLE, FLARE_BLUE joint sub 6 spirals, LIGHT / SHINY+1 sprites, action PLAYER_ATTACK_DEATH_CANNON, gate frame 3). In-game check pending |
| 3 | Fire Blast | 74 | ✓ `ForceJointSkillEffect` (AT_SKILL_SPACE_SPLIT: hidden PIER_PART sub 2 homing, JOINT_FORCE sub 2 pillars with BITMAP_INFERNO, MODEL_SKILL_INFERNO sub 6 rings, strike action family, gate frame 3). In-game check pending |
| 4 | Earth Prison, Blood Howling | 495, 565 | — skipped after Step A: not in 5.2 `_enum.h` |

## Notes for the remaining ports

- Release gate: `AttackStage` `default:` releases magic/melee actions at `AnimationFrame >= 5` (FURY_STRIKE at 1).
- Sounds: release-time sounds are removed from `SkillDefinitions.BuildSkillSounds` and played by the effect.
- Stones `MODEL_STONE1/2` sub 0: `Position += Direction * F` (Direction pre-rotated), fire 1..3 children.
- Per-skill port sheets: scratchpad `port-*.md` (session only).
- Toolkit added in the Summoner pass: `SourceFenrirThunderPool` (MODEL_FENRIR_THUNDER sub 2/3 lightning_type01 instances,
  "_R" bright mesh drawn additive, init BITMAP_LIGHT sprite, one animation key), `SourceHomingThunderPool` (shared, owner-tagged JOINT_THUNDER sub 0/28 homing on a
  walker + Scale 50 arrival children; LightningEffect moved onto it, its smoke now matches BITMAP_SMOKE sub 0).
- Summoner curses: `SourceOwnerBones` (owner bone + double BodyScale as the buff emitters do), `SourceShinyPinPool` (SHINY+6 /
  PIN_LIGHT sub 0 particles), `SourceCastOwnership` (newest cast owns the hand visuals of a caster).
- Also added in the Summoner pass: `SourceBomb3Pool` (CreateBomb3), `SourceModelRenderer.VisibleMeshMask` (per-renderer mesh
  subset without buffer rebuilds), `SourceObjectBlur.Append(batch, light)`, `BuffEffectId.NeilDot / SahamuttDot`.
- Toolkit added in the DL pass: `SourceBombPool` (CreateBomb), `SoundController.PlayBufferSingleChannel` (PlayBuffer on
  one-channel sounds called every tick), `SourceModelRenderer.OverrideAllTextures` (RenderBody texture argument),
  `PlayerObject.GetPacketSkillAnimation` (mount-dependent DL actions also for 0x19 / 0x1E packets).
- Fixed in the DL pass: Power Slash animation id 146 → 145 (PlayerAttackTwoHandSwordTwo; the gate never matched).
- Divergence: C# Dark Horse vehicle plays action 6 / run 2 (`VehicleDatabase`), the original sets 3 for
  PLAYER_ATTACK_DARKHORSE, 2 for ride strike..magic, 1 for run; Earthshake falls back to the rider's frame.
