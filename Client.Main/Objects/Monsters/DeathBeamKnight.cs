using Client.Main.Content;
using Client.Main.Objects.Effects;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(63, "Death Beam Knight")]
    public class DeathBeamKnight : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public DeathBeamKnight()
        {
            Scale = 1.9f;
            MoveSpeed = 250f;
            BlendMesh = -2; // Makes the entire monster semi-transparent like in original
            BlendMeshLight = 1.0f;

            // Eyes: bones 8 (Right), 9 (Left) — same model as BeamKnight
            _eyeGlow = new GlowingEyesEffect
            {
                LeftEyeBone = 9,
                RightEyeBone = 8,
                GlowColor = new Color(60, 140, 255),
                GlowScale = 1.0f,
                GlowAlpha = 0.95f,
                TrailWidth = 5f,
                TrailDuration = 0.7f
            };
            Children.Add(_eyeGlow);
            Children.Add(new MonsterBoneFireEffect
            {
                SourceBones = new[]
                {
                    2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
                    13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23,
                    16, 18, 20, 22, 25, 24, 31, 30
                },
                EmissionRate = 237.5f,
                TexturePath = "Effect/Flame01.jpg",
                TextureColumns = 1,
                SourceParticleSubType = 2,
                ParticleScaleMin = 0.45f,
                ParticleScaleMax = 1.0f,
                ParticleLifetimeFrames = 20f
            });
            Children.Add(new SourceMonsterSandSmokeEffect());
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster45.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.30f);
        }
    }
}
