#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Cancel Stun (AT_SKILL_REMOVAL_STUN = 68) / Cancel Invisibility (AT_SKILL_REMOVAL_INVISIBLE = 71), SourceMain5.2:
    /// gate     ZzzCharacter.cpp:2759   AttackTime = 15 at once (released on the next tick)
    /// release  ZzzCharacter.cpp:4055 / 4111  target != caster: CreateJoint(BITMAP_FLASH sub 0 / 1, target + 1200 Z,
    ///          target angle, Target = target, Scale 120), SOUND_BMS_STUN_REMOVAL (loaded only in Battle Castle)
    /// joint    ZzzEffectJoint.cpp:2531 (MaxTails 10, LifeTime 40, Velocity 70, m_byReverseUV rand%2, MultiUse 150,
    ///          Angle = HeadAngle = (90,0,0)), prologue flight (0,-Velocity,0), 6630 (Velocity += 10F, tail, += 10F,
    ///          += rot(0,-Velocity F,0) F, below ground - 150: clamp, tails stop, Light / 1.2^F; BITMAP_FLARE sprite 1.0
    ///          every frame), epilogue tail
    /// Not drawn on purpose: the MODEL_SKIN_SHELL silhouette (ZzzEffectJoint.cpp:6663, ZzzBMD.cpp:1870) is spawned at model-space
    /// vertices (BMD::Transform with Translate = false), i.e. near the map origin, not at the target.
    /// </summary>
    internal sealed class FlashStrikeEffect : SourceSkillEffectBase
    {
        private const string FlashTexturePath = "Effect/Flashing.jpg";               // BITMAP_FLASH
        private const string FlareTexturePath = "Effect/Flare.jpg";                  // BITMAP_FLARE
        private const string SoundPath = "Sound/BattleCastle/sDStunCancel.wav";      // SOUND_BMS_STUN_REMOVAL
        private const short BattleCastleWorldIndex = 31;

        private readonly WalkerObject _target;
        private SourceTrail? _trail;
        private Texture2D? _flashTexture;
        private Texture2D? _flareTexture;
        private Vector3 _position;
        private Vector3 _light = Vector3.One;
        private float _velocity;
        private float _lifeTime;
        private bool _createTails = true;
        private bool _reverseV;

        public FlashStrikeEffect(WalkerObject caster, WalkerObject target)
            : base(caster, triggerFrame: float.MaxValue, triggerTimeoutSeconds: 0.04f, maxQuads: 2 * 200 + 1)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _flashTexture = await LoadTexture(FlashTexturePath);
            _flareTexture = await LoadTexture(FlareTexturePath);
        }

        private static readonly Vector3 DownAngle = new(90f, 0f, 0f);

        protected override void OnStart()
        {
            _position = _target.WorldPosition.Translation + Vector3.UnitZ * 1200f;
            _velocity = 70f;
            _lifeTime = 40f;
            _reverseV = MuGame.Random.Next(2) == 1;
            _trail = new SourceTrail(SourceJointMath.ScaledMaxTails(10));
            _trail.Push(_position, DownAngle, 120f); // CreateJoint start tail

            if (World?.WorldIndex == BattleCastleWorldIndex)
                SoundController.Instance.PlayBuffer(SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_lifeTime < 0f || _trail == null)
                return;

            // prologue: Velocity != 0 -> Position += rot(0,-Velocity,0) F
            _position += SourceJointMath.Rotate(new Vector3(0f, -_velocity, 0f), DownAngle) * factor;

            _velocity += factor * 10f;
            if (_createTails)
                _trail.Push(_position, DownAngle, 120f);
            _velocity += factor * 10f;
            _position += SourceJointMath.Rotate(new Vector3(0f, -_velocity * factor, 0f), DownAngle) * factor;

            float height = TerrainHeight(_position) - 150f;
            if (_position.Z < height)
            {
                _position.Z = height;
                _createTails = false;
                _light *= MathF.Pow(1f / 1.2f, factor);
            }

            if (_createTails)
                _trail.Push(_position, DownAngle, 120f); // epilogue

            _lifeTime -= factor;
        }

        protected override bool HasActiveContent() => _lifeTime >= 0f;

        protected override void OnDraw()
        {
            if (_trail == null)
                return;

            if (Batch.Begin(_flashTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddTrail(_trail, SourceQuadBatch.TrailFaces.Both, _light, SourceQuadBatch.TrailShade.Flat,
                    _trail.MaxTails - 1, reverseV: _reverseV);
                Batch.End();
            }

            // CreateSprite(BITMAP_FLARE, Position, 1, Light, NULL, rand() % 360) every frame
            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddSprite(_position, 1f, _light, MuGame.Random.Next(360));
                Batch.End();
            }
        }
    }
}
