using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(455, "Giant Mammoth")]
    public class GiantMammoth : RaklionMonsterObject
    {
        protected virtual string ModelPath => "Monster/Monster147.bmd";
        protected override string AttackSoundPath => "Sound/w58w59/GiantMammoth_attack.wav";
        protected override string MoveSoundPath => "Sound/w58w59/GiantMammoth_move.wav";
        protected override string DeathSoundPath => "Sound/w58w59/GiantMammoth_death.wav";

        public GiantMammoth()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare(ModelPath);
            await base.Load();
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World == null || progress.Action != MonsterActionType.Attack2 || !progress.CrossedFrame(8.0) ||
                !TryComputeBoneAt((int)progress.Action, 8.0, 28, out Matrix pose))
                return;

            var impact = (pose * WorldPosition).Translation;
            impact.Z = (World.Terrain?.RequestTerrainHeight(impact.X, impact.Y) ?? Position.Z) + 4f;
            MonsterAttackModelEffect.Spawn(World, "Effect/shockwave_ground01.bmd", impact, Vector3.Zero,
                new Vector3(0.5f, 0.82f, 1f), 0.65f, 0.65f, endScale: 3.2f);
            var shards = new IceShatterEffect(impact, Angle);
            World.Objects.Add(shards);
            _ = shards.Load();
        }
    }
}
