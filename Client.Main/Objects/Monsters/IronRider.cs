using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(352, "Iron Rider")]
    public class IronRider : SoundMonsterObject
    {
        protected override string AttackSoundPath => "Sound/w37/ir_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w37/ir_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/ir_idle-01.wav" : "Sound/w37/ir_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            MuGame.Random.Next(2) == 0 ? "Sound/w37/ir_attack-01.wav" : "Sound/w37/ir_attack-02.wav";

        public IronRider()
        {
            Scale = 1.3f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster109.bmd");
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World?.MapId != 37 || !progress.CrossedTick(10) ||
                !TryGetAttackTargetPosition(out Vector3 target))
                return;

            Matrix[] bones = GetBoneTransforms();
            if (bones == null || bones.Length <= 42)
                return;

            var arrow = new MonsterArrowProjectileEffect((bones[42] * WorldPosition).Translation,
                target + Vector3.UnitZ * 60f, "Effect/ironobj.bmd", 1f);
            World.Objects.Add(arrow);
            _ = arrow.Load();
        }
    }
}
