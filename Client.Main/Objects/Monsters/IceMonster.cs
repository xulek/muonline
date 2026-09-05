using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(22, "Ice Monster")]
    public class IceMonster : MonsterObject
    {
        public IceMonster()
        {
            RenderShadow = true;
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            BlendMesh = 0; // SourceMain5.2: c->Object.BlendMesh = 0
            BlendMeshLight = 1f;
            Blood = false; // SourceMain5.2 CreateBlood(MODEL_ICE_MONSTER): shatters instead of bleeding
        }

        public override async Task Load()
        {
            // Model Loading Type: 15 -> File Number: 15 + 1 = 16
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster16.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 19; (Additional info)
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            // SourceMain5.2 MoveCharacterVisual MODEL_ICE_MONSTER: V-scroll on mesh 0.
            TextureCoordinateOffsetMeshIndex = BlendMesh;
            TextureCoordinateOffset = new Vector2(
                0f,
                -((long)gameTime.TotalGameTime.TotalMilliseconds % 2000L) * 0.0005f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 50, 51, 50, 50, 52)
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mIceMonster1.wav"
                : "Sound/mIceMonster2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mIceMonster1.wav", Position, listenerPosition);
            // Index 3 -> Sound 50
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mIceMonster1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mIceMonsterDie.wav", Position, listenerPosition);

            if (World != null)
            {
                // SourceMain5.2 CreateBlood: o->Live = false + 10x MODEL_ICE_SMALL —
                // the corpse is replaced immediately by ice shards.
                var shatter = new Effects.IceShatterEffect(Position, Angle);
                World.Objects.Add(shatter);
                _ = shatter.Load();
                Hidden = true;
            }
        }
    }
}
