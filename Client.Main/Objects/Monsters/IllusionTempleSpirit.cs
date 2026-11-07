using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    public abstract class IllusionTempleSpirit : SoundMonsterObject
    {
        protected virtual bool IsPoison => false;
        private bool IsIllusionTemple => World?.MapId is >= 45 and <= 50;
        private int _soundAction = -1;
        private double _soundFrame = -1;
        protected override string DamageSoundPath => !IsIllusionTemple ? null : IsPoison
            ? "Sound/w47/cursedtemple_damage02.wav" : "Sound/w47/cursedtemple_damage01.wav";
        protected override string DeathSoundPath => !IsIllusionTemple ? null : IsPoison
            ? "Sound/w47/cursedtemple_death02.wav" : "Sound/w47/cursedtemple_death01.wav";

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!IsIllusionTemple || Model?.Actions == null || CurrentAction < 0 ||
                CurrentAction >= Model.Actions.Length || Model.Actions[CurrentAction] == null ||
                (CurrentAction != (int)MonsterActionType.Stop1 && CurrentAction != (int)MonsterActionType.Walk))
            {
                _soundAction = -1;
                _soundFrame = -1;
                return;
            }

            var action = Model.Actions[CurrentAction];
            int frames = System.Math.Max(action.LockPositions ? action.NumAnimationKeys - 1 : action.NumAnimationKeys, 1);
            double frame = GetAnimationTime() % frames;
            double previous = _soundAction == CurrentAction ? _soundFrame : -1;
            _soundAction = CurrentAction;
            _soundFrame = frame;
            if (!CrossedSoundFrame(previous, frame))
                return;

            if (CurrentAction == (int)MonsterActionType.Walk)
                PlayMonsterSound("Sound/w47/cursedtemple_move01.wav");
            else if (!IsPoison)
                PlayMonsterSound("Sound/w47/cursedtemple_idle01.wav");
        }

        private static bool CrossedSoundFrame(double previous, double current) => previous < 0
            ? current > 0.5 && current < 1.5
            : current < previous ? previous <= 0.5 || current > 0.5 : previous <= 0.5 && current > 0.5;
    }

    [NpcInfo(389, "Illusion Spirit (Lightning)")] internal class LightningSpirit2 : RedIllusionSorcereSpirit { }
    [NpcInfo(392, "Illusion Spirit (Lightning)")] internal class LightningSpirit3 : RedIllusionSorcereSpirit { }
    [NpcInfo(395, "Illusion Spirit (Lightning)")] internal class LightningSpirit4 : RedIllusionSorcereSpirit { }
    [NpcInfo(398, "Illusion Spirit (Lightning)")] internal class LightningSpirit5 : RedIllusionSorcereSpirit { }
    [NpcInfo(401, "Illusion Spirit (Lightning)")] internal class LightningSpirit6 : RedIllusionSorcereSpirit { }
    [NpcInfo(390, "Illusion Spirit (Ice)")] internal class IceSpirit2 : BlueIllusionSorcereSpirit { }
    [NpcInfo(393, "Illusion Spirit (Ice)")] internal class IceSpirit3 : BlueIllusionSorcereSpirit { }
    [NpcInfo(396, "Illusion Spirit (Ice)")] internal class IceSpirit4 : BlueIllusionSorcereSpirit { }
    [NpcInfo(399, "Illusion Spirit (Ice)")] internal class IceSpirit5 : BlueIllusionSorcereSpirit { }
    [NpcInfo(402, "Illusion Spirit (Ice)")] internal class IceSpirit6 : BlueIllusionSorcereSpirit { }
    [NpcInfo(391, "Illusion Spirit (Poison)")] internal class PoisonSpirit2 : IllusionSorcereSpirit3 { }
    [NpcInfo(394, "Illusion Spirit (Poison)")] internal class PoisonSpirit3 : IllusionSorcereSpirit3 { }
    [NpcInfo(397, "Illusion Spirit (Poison)")] internal class PoisonSpirit4 : IllusionSorcereSpirit3 { }
    [NpcInfo(400, "Illusion Spirit (Poison)")] internal class PoisonSpirit5 : IllusionSorcereSpirit3 { }
    [NpcInfo(403, "Illusion Spirit (Poison)")] internal class PoisonSpirit6 : IllusionSorcereSpirit3 { }
}
