using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(355, "Kentauros")]
    public class Kentauros : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w37/kenta_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/kenta_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/kenta_idle-01.wav" : "Sound/w37/kenta_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w37/kenta_skill-01.wav" : AttackSoundPath;

        public Kentauros()
        {
            Scale = 1.1f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster112.bmd");
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World?.MapId != 37 || !progress.CrossedTick(14))
                return;

            bool spread = progress.Action == MonsterActionType.Attack2;
            int sourceBone = spread ? 27 : 29;
            Matrix[] bones = GetBoneTransforms();
            if (bones == null || bones.Length <= sourceBone)
                return;

            Vector3 local = spread ? new Vector3(60f, -30f, 50f) : new Vector3(30f, -30f, 0f);
            Vector3 origin = Vector3.Transform(local, bones[sourceBone] * WorldPosition);
            MonsterAttackModelEffect.Spawn(World, "Effect/cantasarrow.bmd", origin, Angle, Vector3.One,
                0.55f, 1f, speed: 1350f, rays: spread ? 3 : 1, yawStep: MathHelper.ToRadians(15f));
        }
    }
}
