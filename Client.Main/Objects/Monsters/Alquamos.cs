using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
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
    [NpcInfo(69, "Alquamos")]
    public class Alquamos : MonsterObject
    {
        public Alquamos()
        {
            Scale = 1.0f;
            MoveSpeed = 250f;
            BlendMesh = 0; // SourceMain5.2 CreateCharacter: BlendMesh = 0
            BlendMeshLight = 1.0f;

            // SourceMain5.2 RenderCharacter(MONSTER_ALQUAMOS): BITMAP_LIGHT sprites
            // (scale 0.6, bluish-white) on the first 9 g_chStar bones with flickering
            // luminosity in the 0.7..1.0 range (approximated here with a fast pulse).
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 10, 18, 37, 38, 51, 52, 58, 59, 66 },
                PrimaryTexturePath = "Effect/light.jpg",
                PrimaryScale = 0.6f,
                LightColor = new Color(204, 229, 255),
                PulseBase = 0.85f,
                PulseAmplitude = 0.15f,
                PulseSpeed = 0.02f,
                HideDuringDeath = true
            });

            // SourceMain5.2 RenderCharacter(MONSTER_ALQUAMOS): every frame, 3x
            // CreateParticle(BITMAP_SPARK+1, sub 3) on random bones with ±10 offset.
            Children.Add(new MonsterBoneFireEffect
            {
                TexturePath = "Effect/Spark03.jpg",
                TextureColumns = 1,
                SourceParticleSubType = 3,
                RandomBone = true,
                SourceOffsets = new[]
                {
                    new Vector3(10f, 0f, 0f), new Vector3(-10f, 0f, 0f),
                    new Vector3(0f, 10f, 0f), new Vector3(0f, -10f, 0f),
                    new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, -10f),
                },
                EmissionRate = 75f,
                ParticleLight = new Vector3(0.6f, 0.7f, 0.8f),
                ParticleScaleMin = 0.2f,
                ParticleScaleMax = 0.3f,
                ParticleLifetimeFrames = 12f
            });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster51.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 175, 175, 175, 175, 176);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            // Note: C++ had commented out idle sound, but we'll use attack sound for idle as per mapping
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mAlquamosAttack1.wav", Position, listenerPosition); // Sound 175
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mAlquamosAttack1.wav", Position, listenerPosition); // Sound 175

            // NOTE: SourceMain5.2 AttackEffect has no visual for a plain Alquamos attack
            // (MONSTER_ALQUAMOS case is an empty break). Its 4x CreateJoint(BITMAP_FLARE,
            // sub7) branch runs only under AT_SKILL_ENERGYBALL — a skill state the server
            // never sends the port — and those stationary joints collapse to invisible
            // degenerate ribbons even in the original. So: sound only, like the original.
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mAlquamosDie.wav", Position, listenerPosition); // Sound 176
        }
    }
}
