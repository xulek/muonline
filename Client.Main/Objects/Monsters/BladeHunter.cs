using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(354, "Blade Hunter")]
    public class BladeHunter : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w37/blade_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/blade_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/blade_idle-01.wav" : "Sound/w37/blade_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/blade_attack-01.wav" : "Sound/w37/blade_attack-02.wav";

        public BladeHunter()
        {
            Scale = 1.3f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster111.bmd");
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World?.MapId != 37 || !progress.CrossedTick(14))
                return;

            Matrix[] bones = GetBoneTransforms();
            if (bones == null || bones.Length <= 12)
                return;

            Vector3 origin = Vector3.Transform(new Vector3(140f, 0f, -30f), bones[12] * WorldPosition);
            MonsterAttackModelEffect.Spawn(World, "Effect/bladetonedo.bmd", origin,
                Angle, new Vector3(0.38f, 0.52f, 1f), 0.75f, 1f, speed: 420f, animated: true);
        }
    }
}
