using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(459, "Selupan")]
    public class SelupanBoss : RaklionMonsterObject
    {
        private static readonly Vector3 IceTint = new(0.25f, 0.52f, 1f);
        private static readonly Vector3 WindTint = new(0.3f, 0.9f, 0.4f);
        private readonly MonsterBreathEffect _poisonBreath = new() { SourceBone = 0, MinScale = 0.7f, MaxScale = 1.1f };

        protected override string DeathSoundPath => "Sound/w58w59/Selupan_word2.wav";

        public SelupanBoss()
        {
            Scale = 2f;
            BlendMesh = 4;
            Children.Add(_poisonBreath);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Monster/Monster415.bmd");
            await base.Load();
        }

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            1 or 2 => "Sound/w58w59/Selupan_attack1.wav",
            3 => "Sound/w58w59/Selupan_attack2.wav",
            _ => null,
        };

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (World?.MapId == 58)
                SpawnIceRain(Position);
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World?.MapId != 58)
                return;

            switch (progress.Action)
            {
                case MonsterActionType.Attack1 when progress.CrossedFrame(3.7) &&
                                                    TryGetAttackTargetPosition(out Vector3 target):
                    var orb = new MonsterArrowProjectileEffect(Position + Vector3.UnitY * 30f,
                        target + Vector3.UnitZ * 60f, "Effect/serufan_magic.bmd", 1.5f);
                    World.Objects.Add(orb);
                    _ = orb.Load();
                    SpawnIceRain(target);
                    break;

                case MonsterActionType.Attack2:
                    if (progress.CrossedFrame(1.0) || progress.CrossedFrame(4.0))
                        _poisonBreath.EmitBurst(new Color(WindTint), count: 9, speed: 175f);
                    if (progress.CrossedFrame(6.8))
                        MonsterAttackModelEffect.Spawn(World, "Effect/chysukmoon.bmd",
                            Position + Vector3.UnitZ * 100f, Angle, WindTint, 0.9f, 0.4f,
                            speed: 750f, rays: 5, yawStep: MathHelper.ToRadians(20f));
                    break;

                case MonsterActionType.Attack3 when progress.CrossedFrame(6.6):
                    MonsterAttackModelEffect.Spawn(World, "Effect/knight_plancrack_grand.bmd",
                        Position + Vector3.UnitZ * 4f, Angle, IceTint, 1.2f, 2f, animated: true);
                    MonsterAttackModelEffect.Spawn(World, "Skill/Inferno01.bmd",
                        Position, Angle, IceTint, 0.85f, 1.25f, endScale: 1.8f);
                    break;

                case MonsterActionType.Attack4 when progress.SkillStarted:
                    PlaySpecialCast(progress.SkillId);
                    break;
            }
        }

        private void SpawnIceRain(Vector3 center)
        {
            var rain = new MonsterBlizzardStrikeEffect(center, wide: true);
            World.Objects.Add(rain);
            _ = rain.Load();
        }

        private void PlaySpecialCast(ushort skill)
        {
            switch (skill)
            {
                case 38:
                    PlayAttackSound("Sound/w58w59/Selupan_word4.wav");
                    break;
                case 39:
                    PlayAttackSound("Sound/w58w59/Selupan_cure.wav");
                    var heal = new HealingCircleEffect(this, this, 3, sound: null);
                    heal.ReleaseImmediately();
                    World.Objects.Add(heal);
                    _ = heal.Load();
                    break;
                case 40:
                    PlayAttackSound("Sound/w58w59/Selupan_word3.wav");
                    break;
                case 42:
                    var tint = new Vector3(0.65f, 0.36f, 0.18f);
                    MonsterAttackModelEffect.Spawn(World, "Skill/boswind.bmd", Position,
                        Angle, tint, 1.25f, 1.6f, animated: true);
                    MonsterAttackModelEffect.Spawn(World, "Skill/nightmaresum.bmd", Position,
                        Angle, tint, 1.25f, 1.2f, endScale: 1.5f);
                    break;
            }
        }
    }
}
