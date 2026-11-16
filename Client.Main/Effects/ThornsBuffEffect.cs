#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Effects
{
    /// <summary>
    /// Thorns / Damage Reflection status (eBuff_Thorns = 71), SourceMain5.2:
    /// spawn   WSclient.cpp:15120 / ZzzCharacter.cpp:10636 CreateJoint(BITMAP_FLARE, sub 43, owner, 50, (0.9, 0.6, 0.1));
    ///         WSclient.cpp:15336 DeleteJoint(BITMAP_FLARE, o, 43) removes the emitter only
    /// emitter ZzzEffectJoint.cpp:1629 / 1981 (Scale stays 10, no tails), 5829 (rand_fps_check(3): owner + (rand%60-30)F on X / Y,
    ///         +30F Z, CreateJoint(BITMAP_FLARE sub 44, owner, Scale 10, Light))
    /// sub 44  ZzzEffectJoint.cpp:1987 (LifeTime 15, MaxTails 30, Direction[2] rand%2+2, TargetPosition = spawn), 5850
    ///         (Direction[2] += F, Z += Direction[2]*F, Light *= (1/1.05)^F, BITMAP_PIN_LIGHT sprites 1.5 / 0.5, dead at Z &gt;= 400
    ///         or without the owner), 5878 (LifeTime &lt; 10: Light *= (1/1.3)^F), epilogue CreateTail; Flare.jpg ribbon, both faces.
    /// </summary>
    internal sealed class ThornsBuffEffect : SourceSkillEffectBase
    {
        private const string FlareTexturePath = "Effect/Flare.jpg";      // BITMAP_FLARE
        private const string PinTexturePath = "Effect/pin_lights.jpg";   // BITMAP_PIN_LIGHT
        private static readonly Vector3 EmitterLight = new(0.9f, 0.6f, 0.1f);

        private struct Flare
        {
            public ThornsBuffEffect? Owner;
            public Vector3 Position;
            public Vector3 Light;
            public float Rise;
            public float LifeTime;
            public SourceTrail Trail;
        }

        // Shared across owners so the 30-tail trails are allocated once (about 5 live per owner).
        private static readonly Flare[] SharedFlares = new Flare[96];

        private Texture2D? _flareTexture, _pinTexture;
        private Vector3 _angle;
        private int _count;
        private bool _stopped;

        public ThornsBuffEffect(WalkerObject owner)
            : base(owner, 0f, 0f, 512)
        {
        }

        /// <summary>Status ended: DeleteJoint removes the emitter, live flares finish.</summary>
        public void Stop() => _stopped = true;

        protected override async Task LoadEffectContent()
        {
            _flareTexture = await LoadTexture(FlareTexturePath);
            _pinTexture = await LoadTexture(PinTexturePath);
        }

        protected override void OnStart()
        {
            _angle = new Vector3(MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));
        }

        protected override void OnTick(float factor)
        {
            Position = CasterPosition;

            if (!_stopped && !Caster.Hidden && FPSCounter.Instance.RandFPSCheck(3))
            {
                Vector3 position = CasterPosition;
                position.Z += 30f * factor;
                position.X += (MuGame.Random.Next(60) - 30) * factor;
                position.Y += (MuGame.Random.Next(60) - 30) * factor;
                Spawn(position);
            }

            if (_count == 0)
                return;
            float fade = MathF.Pow(1f / 1.05f, factor);
            float endFade = MathF.Pow(1f / 1.3f, factor);
            for (int i = 0; i < SharedFlares.Length; i++)
            {
                ref Flare f = ref SharedFlares[i];
                if (!ReferenceEquals(f.Owner, this))
                    continue;

                f.Rise += factor;
                f.Position.Z += f.Rise * factor;
                f.Light *= fade;
                bool dead = f.Position.Z >= 400f;
                if (f.LifeTime < 10f)
                    f.Light *= endFade;
                f.Trail.Push(f.Position, _angle, 10f);
                f.LifeTime -= factor;
                if (dead || f.LifeTime < 0f)
                {
                    f.Owner = null;
                    _count--;
                }
            }
        }

        private void Spawn(Vector3 position)
        {
            int maxTails = SourceJointMath.ScaledMaxTails(30);
            for (int i = 0; i < SharedFlares.Length; i++)
            {
                ref Flare f = ref SharedFlares[i];
                if (f.Owner != null)
                    continue;
                f.Owner = this;
                f.Position = position;
                f.Light = EmitterLight;
                f.Rise = MuGame.Random.Next(2) + 2;
                f.LifeTime = 15f;
                if (f.Trail == null)
                    f.Trail = new SourceTrail(maxTails);
                else
                    f.Trail.Reset(maxTails);
                f.Trail.Push(position, _angle, 10f);
                _count++;
                return;
            }
        }

        protected override bool HasActiveContent() => !_stopped || _count > 0;

        protected override void OnDraw()
        {
            if (_count == 0)
                return;

            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < SharedFlares.Length; i++)
                {
                    ref Flare f = ref SharedFlares[i];
                    if (ReferenceEquals(f.Owner, this))
                        Batch.AddTrail(f.Trail, SourceQuadBatch.TrailFaces.Both, f.Light, SourceQuadBatch.TrailShade.Flat, f.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            if (Batch.Begin(_pinTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < SharedFlares.Length; i++)
                {
                    ref Flare f = ref SharedFlares[i];
                    if (!ReferenceEquals(f.Owner, this))
                        continue;
                    Batch.AddSprite(f.Position, 1.5f, f.Light, 0f);
                    Batch.AddSprite(f.Position, 0.5f, f.Light, 0f);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            if (_count == 0)
                return;
            for (int i = 0; i < SharedFlares.Length; i++)
                if (ReferenceEquals(SharedFlares[i].Owner, this))
                    SharedFlares[i].Owner = null;
            _count = 0;
        }
    }
}
