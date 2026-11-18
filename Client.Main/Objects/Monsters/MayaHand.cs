using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    public abstract class MayaHand : SoundMonsterObject
    {
        protected override bool VaryBasicAttack => World?.MapId != 39;
        private const string SlamSound = "Sound/w39/maya_hand_attack-01.wav";
        private const string WaveSound = "Sound/w39/maya_hand_attack-02.wav";
        protected abstract Vector3 AttackTint { get; }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (!progress.CrossedTick(14) || World == null)
                return;

            if (progress.Action == MonsterActionType.Attack1)
            {
                var ground = Position;
                ground.Z = World.Terrain?.RequestTerrainHeight(ground.X, ground.Y) ?? ground.Z;
                MonsterAttackModelEffect.Spawn(World, "Skill/Inferno01.bmd", ground, Angle, AttackTint, 0.6f, 1.5f);
                MonsterAttackModelEffect.Spawn(World, "Effect/shockwave_ground01.bmd",
                    ground + Vector3.UnitZ * 4f, Vector3.Zero, AttackTint, 0.75f, 0.6f, endScale: 5f);
                PlayAttackSound(SlamSound);
            }
            else if (progress.Action == MonsterActionType.Attack2)
            {
                Vector3 origin = Position - Vector3.UnitY * 160f;
                MonsterAttackModelEffect.Spawn(World, "Skill/hendlight02.bmd",
                    origin, Angle, AttackTint, 0.8f, 1f, endScale: 1.8f, speed: 1900f);
                PlayAttackSound(WaveSound);
            }
        }
    }
}
