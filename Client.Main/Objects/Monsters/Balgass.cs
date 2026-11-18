using Client.Main.Content;
using Client.Main.Objects.Effects;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(349, "Balgass")]
    public class Balgass : SoundMonsterObject
    {
        private GlowingEyesEffect _eyeGlow;
        private const int WeaponBone = 37; // ar1.jpg mesh is skinned to Box05 in Monster90.
        protected override bool VaryBasicAttack => World?.MapId != 34;

        protected override string AttackSoundPath => "Sound/w35/balga_at1.wav";
        protected override string DeathSoundPath => "Sound/w35/balga_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w35/balga_idle1.wav" : "Sound/w35/balga_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            2 => "Sound/w35/balga_at2.wav",
            3 => "Sound/w35/balga_skill1.wav",
            4 => "Sound/w35/balga_skill2.wav",
            _ => AttackSoundPath
        };

        public Balgass()
        {
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 10, RightEyeBone = 9, GlowColor = new Color(50, 130, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster90.bmd");
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World == null)
                return;

            if (progress.Action == MonsterActionType.Attack1 && progress.CrossedFrame(3.5))
            {
                MonsterAttackTrailEffect.Spawn(this, progress.Action, 2.7f, 5.1f,
                    WeaponBone, WeaponBone, new Vector3(0.18f, 0.48f, 1f),
                    new Vector3(0f, 0f, -60f), new Vector3(0f, 0f, -150f));
            }
            else if (progress.Action == MonsterActionType.Attack2 && progress.CrossedFrame(7.5) &&
                     TryComputeBoneAt((int)progress.Action, 7.5, 33, out Matrix pose))
            {
                Vector3 impact = (pose * WorldPosition).Translation;
                Vector3 ground = impact;
                ground.Z = (World.Terrain?.RequestTerrainHeight(ground.X, ground.Y) ?? Position.Z) + 4f;
                MonsterAttackModelEffect.Spawn(World, "Effect/knight_plancrack_a.bmd", ground, Angle,
                    new Vector3(0.65f, 0.58f, 0.48f), 0.9f, 1f, animated: true);
                var sparks = new MonsterHitSparkEffect(impact);
                World.Objects.Add(sparks);
                _ = sparks.Load();
            }
        }
    }
}
