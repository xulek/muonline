using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(304, "Witch Queen")]
    public class WitchQueen : SoundMonsterObject
    {
        protected override string DeathSoundPath => "Sound/w34/wq_death.wav";
        protected virtual string ModelPath => "Monster/Monster101.bmd";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w34/wq_idle1.wav" : "Sound/w34/wq_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w34/wq_attack1.wav" : "Sound/w34/wq_attack2.wav";

        public WitchQueen()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare(ModelPath);
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World?.MapId != 33 || progress.Action != MonsterActionType.Attack2 || !progress.CrossedTick(10))
                return;

            for (int i = 0; i < 5; i++)
            {
                float yaw = Angle.Z + MathHelper.TwoPi * i / 5f;
                Vector3 point = Position + new Vector3(MathF.Cos(yaw) * 250f, MathF.Sin(yaw) * 250f, 0f);
                point.Z = World.Terrain?.RequestTerrainHeight(point.X, point.Y) ?? Position.Z;
                var bolt = SourceJointEffect.ThunderHoming(point + Vector3.UnitZ * 250f, () => point, 65f);
                bolt.LightTint = new Vector3(0.62f, 0.75f, 1f);
                World.Objects.Add(bolt);
                _ = bolt.Load();
            }
        }
    }
}
