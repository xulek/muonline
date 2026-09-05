using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(77, "Phoenix Of Darkness")]
    public class PhoenixOfDarkness : MonsterObject
    {
        private readonly PhoenixBodyObject _body;

        public PhoenixOfDarkness()
        {
            Scale = 1.0f;
            MoveSpeed = 250f;
            RenderShadow = false;

            _body = new PhoenixBodyObject
            {
                RenderShadow = false
            };
            Children.Add(_body);
        }

        public override async Task Load()
        {
            // SourceMain5.2 CreateMonster(MONSTER_DARK_PHOENIX): MODEL_DARK_PHEONIX_SHIELD = Monster56.bmd,
            // MODEL_DARK_PHOENIX = Monster57.bmd. The two skeletons differ (61 vs 48 bones),
            // so the body is drawn as a child object animated with the same actions.
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster57.bmd");
            _body.Model = await BMDLoader.Instance.Prepare($"Monster/Monster56.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);

            // Monster56 and Monster57 use different skeletons (61 vs 48 bones).
            // Keep the original action timing, but never share Monster57's bone palette
            // with the body model.
            _body.CopyActionSpeedsFrom(this);
        }

        public override void Update(GameTime gameTime)
        {
            // SourceMain5.2 RenderCharacter(MONSTER_DARK_PHOENIX): the shield body glows
            // on mesh 0 with a pulsing light:
            //   s = 0.5 * (1 + sin((WorldTime % 10000) * 0.001));
            //   BlendMeshLight = 0.3 * (1 - s) + 0.3;
            double worldTimeMs = gameTime.TotalGameTime.TotalMilliseconds % 10000.0;
            float s = 0.5f * (1f + MathF.Sin((float)worldTimeMs * 0.001f));
            _body.BlendMesh = 0;
            _body.BlendMeshLight = 0.3f * (1f - s) + 0.3f;

            base.Update(gameTime);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 183, 184, 185, 185, -1);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhoenix1.wav", Position, listenerPosition); // Sound 183
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhoenixAttack1.wav", Position, listenerPosition); // Sound 185
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhoenixAttack1.wav", Position, listenerPosition);
        }

        // Note: No death sound according to C++ mapping (death sound index was -1)

        private sealed class PhoenixBodyObject : ModelObject
        {
            public PhoenixBodyObject()
            {
                // MonsterObject uses this animation speed for the Phoenix root.
                AnimationSpeed = 6f;
            }

            public override void Update(GameTime gameTime)
            {
                if (Parent is PhoenixOfDarkness phoenix)
                {
                    CurrentAction = phoenix.CurrentAction;
                    HoldOnLastFrame = CurrentAction == (int)MonsterActionType.Die || phoenix.IsDead;
                }

                // SourceMain5.2 MoveCharacterVisual MODEL_DARK_PHEONIX_SHIELD:
                // V-scroll on the shield mesh.
                TextureCoordinateOffsetMeshIndex = 0;
                TextureCoordinateOffset = new Vector2(
                    0f,
                    ((long)gameTime.TotalGameTime.TotalMilliseconds % 10000L) * 0.0001f);

                base.Update(gameTime);
            }

            public void CopyActionSpeedsFrom(ModelObject source)
            {
                if (source?.Model?.Actions == null || Model?.Actions == null)
                    return;

                int count = Math.Min(source.Model.Actions.Length, Model.Actions.Length);
                for (int i = 0; i < count; i++)
                {
                    if (source.Model.Actions[i] != null && Model.Actions[i] != null)
                        Model.Actions[i].PlaySpeed = source.Model.Actions[i].PlaySpeed;
                }
            }
        }
    }
}
