using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(131, "Castle Gate")]
    public class BloodCastleGate : MonsterObject
    {
        public BloodCastleGate()
        {
            Scale = 0.8f;
            RenderShadow = false;
        }

        private bool _destroyed;

        /// <summary>
        /// ZzzObject.cpp:1415 (MODEL_CASTLE_GATE, MONSTER01_DIE): o->Live = false, SOUND_HIT_GATE2 and
        /// RenderMeshEffect(0, MODEL_GATE) on the same frame: the gate vanishes and breaks into fragments.
        /// </summary>
        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            if (_destroyed)
                return;

            _destroyed = true;
            HiddenMesh = -2;
            Hidden = true;
            Interactive = false;
            SoundController.Instance.PlayBuffer("Sound/eHitGate2.wav");
            BloodCastleDeathFragmentEffect.Spawn(this, "Object12/Gate01.bmd", "Object12/Gate02.bmd");
        }

        public override void Update(GameTime gameTime)
        {
            if (_destroyed)
            {
                World?.RemoveObject(this);
                Dispose();
                return;
            }

            base.Update(gameTime);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster62.bmd");
            await base.Load();
        }

        protected override void RecalculateWorldPosition()
        {
            base.RecalculateWorldPosition();
            if (Parent != null)
                return;

            Matrix worldPosition = WorldPosition;
            worldPosition.Translation += new Vector3(0f, 60f, 0f);
            WorldPosition = worldPosition;
        }
    }
}
