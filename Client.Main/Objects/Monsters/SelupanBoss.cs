using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Controllers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    public class SelupanBoss : MonsterObject
    {
        public SelupanBoss()
        {
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster415.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            BlendMesh = 4;
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            if (World is not WalkableWorldControl world || !TryConsumeAttackEffectWindow())
                return;

            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHellSpiderAttack1.wav", Position, listenerPosition);

            // SourceMain5.2 GM_Raklion.cpp:511-522 ATTACK1 — MODEL_RAKLION_BOSS_MAGIC orb
            // (Effect/serufan_magic.bmd, scale 1.5) spawns at Position + 30Y and flies
            // toward the target. (The matching 20x BROKEN_ICE rain over the hero has
            // no port equivalent yet.)
            var orb = new Effects.MonsterArrowProjectileEffect(this, 0, LastAttackTargetId)
            {
                ProjectileModelPath = "Effect/serufan_magic.bmd",
                ProjectileScale = 1.5f,
                SourceOffset = new Vector3(0f, 30f, 0f)
            };
            world.Objects.Add(orb);
            _ = orb.Load();
        }
    }
}
