#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Effects
{
    /// <summary>
    /// Rage Fighter buff statuses (eBuff_Att / Hp / Def_up_Ourforces = 129 / 130 / 131), SourceMain5.2:
    /// spawn   WSclient.cpp:4724: BITMAP_LIGHT_RED sub 0 (Hp) / 1 (Att) / 2 (Def); Def also MODEL_WINDFOCE sub 1
    /// red     ZzzEffect.cpp:6151, 17331 (alive while the buff is registered; Att: BITMAP_SMOKELINE1 sub 5 on bone 26 / 35
    ///         every tick, Scale 0.005, Light (1, 0.12, 0); Def: rand % 70 == 7 -> MODEL_WINDFOCE_MIRROR when none exists),
    ///         18930 (sub 0: decal x2 Scale 3.5 + BITMAP_LIGHT_RED sprites Scale 1.5 on 17 bones, WorldTime pulses)
    /// wind    ZzzEffect.cpp:17268 (sub 1: Light (0.6,0.6,1) * ((sin(WorldTime*0.001)+1)*0.25+0.2), Scale 1), ZzzObject.cpp:2527
    /// mirror  ZzzEffect.cpp:6182 (LifeTime 50), 17393 (Angle.z = WorldTime*0.4, Scale *= 1.015^F max 1.5, Light *= 0.9), ZzzObject.cpp:2533
    /// smoke   ZzzEffectParticle.cpp:1781 (LifeTime 35, Scale (rand%96+96)*0.01*Scale, Gravity (rand%16+12)*0.1), 5997, default sprite
    /// Sub 1 / 2 of BITMAP_LIGHT_RED have no render branch.
    /// </summary>
    internal sealed class RageBuffAuraEffect : SourceSkillEffectBase
    {
        public enum Kind { Attack, Health, Defense }

        private const string RedLightTexturePath = "Effect/flare01_red.jpg";    // BITMAP_LIGHT_RED
        private const string SmokeTexturePath = "Effect/smokelines01.jpg";      // BITMAP_SMOKELINE1
        private const string WindPath = "Effect/wind_foce.bmd";                 // MODEL_WINDFOCE
        private const string MirrorPath = "Effect/wind_foce_mirror.bmd";        // MODEL_WINDFOCE_MIRROR
        private static readonly int[] SpriteBones = { 12, 17, 5, 10, 36, 27, 37, 28, 11, 35, 2, 3, 36, 20, 27, 4, 26 };
        private static readonly Vector3 WindLight = new(0.6f, 0.6f, 1f);

        private struct Smoke
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 StartPosition;
            public float Alpha;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Kind _kind;
        private readonly Smoke[] _smokes = new Smoke[48];
        private SourceModelRenderer? _windRenderer;
        private SourceModelRenderer? _mirrorRenderer;
        private Texture2D? _redLightTexture;
        private Texture2D? _smokeTexture;
        private float _decalRotation;
        private bool _mirrorLive;
        private Vector3 _mirrorLight;
        private float _mirrorScale;
        private float _mirrorLife;
        private int _smokeCount;
        private bool _stopped;

        public RageBuffAuraEffect(WalkerObject owner, Kind kind)
            : base(owner, 0f, 0f, 128)
        {
            _kind = kind;
        }

        /// <summary>Status ended: the emitter is deleted, live particles finish.</summary>
        public void Stop() => _stopped = true;

        protected override async Task LoadEffectContent()
        {
            if (_kind == Kind.Health)
            {
                _redLightTexture = await LoadTexture(RedLightTexturePath);
            }
            else if (_kind == Kind.Attack)
            {
                _smokeTexture = await LoadTexture(SmokeTexturePath);
            }
            else
            {
                _windRenderer = await LoadModel(WindPath);
                _mirrorRenderer = await LoadModel(MirrorPath);
                if (_windRenderer != null) _windRenderer.AnimationSpeed = 0f;
                if (_mirrorRenderer != null) _mirrorRenderer.AnimationSpeed = 0f;
            }
        }

        protected override void OnStart()
        {
            _decalRotation = MathHelper.ToDegrees(Caster.Angle.X);
        }

        protected override void OnTick(float factor)
        {
            if (!_stopped)
            {
                if (_kind == Kind.Attack)
                {
                    int bone = FpsCheck(2, factor) ? 26 : 35;
                    if (FpsCheck(1, factor))
                        SpawnSmoke(GetCasterBonePosition(bone));
                }
                else if (_kind == Kind.Defense && !_mirrorLive && MuGame.Random.Next(70) == 7 && FpsCheck(1, factor))
                {
                    _mirrorLive = true;
                    _mirrorLight = WindLight;
                    _mirrorScale = 1f;
                    _mirrorLife = 50f;
                }
            }

            if (_mirrorLive)
            {
                _mirrorScale = MathF.Min(1.5f, _mirrorScale * MathF.Pow(1.015f, factor));
                _mirrorLight *= 0.9f; // per frame
                _mirrorLife -= factor;
                if (_mirrorLife <= 0f || _stopped)
                    _mirrorLive = false;
            }

            TickSmokes(factor);
        }

        private void SpawnSmoke(Vector3 position)
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke s = ref _smokes[i];
                if (s.Live)
                    continue;

                s.Live = true;
                s.Position = position;
                s.StartPosition = CasterPosition;
                s.LifeTime = 35f;
                s.Scale = (MuGame.Random.Next(96) + 96) * 0.01f * 0.005f;
                s.Rotation = MuGame.Random.Next(360);
                s.Gravity = (MuGame.Random.Next(16) + 12) * 0.1f;
                s.Alpha = 0f;
                _smokeCount++;
                return;
            }
        }

        private void TickSmokes(float factor)
        {
            if (_smokeCount == 0)
                return;

            Vector3 owner = CasterPosition;
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke s = ref _smokes[i];
                if (!s.Live)
                    continue;

                s.LifeTime -= factor;
                bool dead = s.LifeTime <= 0f;
                if (!dead)
                {
                    if (s.LifeTime < 10f)
                        s.Alpha -= factor * 0.1f;
                    else if (s.Alpha < 0.7f)
                        s.Alpha += factor * (MuGame.Random.Next(5) + 2) * 0.1f;
                    else
                        s.Alpha = 0.7f;

                    if (s.Alpha < 0.1f)
                        dead = true;

                    s.Scale += (MuGame.Random.Next(3) + 6) * 0.01f * factor;
                    s.Position.Z += s.Gravity * factor;
                    s.Position += owner - s.StartPosition;
                    s.StartPosition = owner;
                }

                if (dead)
                {
                    s.Live = false;
                    _smokeCount--;
                }
            }
        }

        protected override bool HasActiveContent() => !_stopped || _smokeCount > 0;

        protected override void OnDraw()
        {
            double worldTime = FPSCounter.Instance.WorldTime;
            Vector3 owner = CasterPosition;

            if (_kind == Kind.Health && !_stopped && _redLightTexture != null &&
                Batch.Begin(_redLightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                var decalLight = new Vector3((float)(Math.Sin(worldTime * 0.0005) + 1.0) * 0.25f + 0.2f);
                Batch.AddTerrainDecal(World?.Terrain, owner.X, owner.Y, 3.5f, 3.5f, decalLight, _decalRotation);
                Batch.AddTerrainDecal(World?.Terrain, owner.X, owner.Y, 3.5f, 3.5f, decalLight, _decalRotation);

                float slow = (float)(Math.Sin(worldTime * 0.01) + 1.0);
                float mid = (float)(Math.Sin(worldTime * 0.005) + 1.0) * 0.25f + 0.5f;
                for (int i = 0; i < SpriteBones.Length; i++)
                {
                    float fade;
                    if (i <= 2)
                        fade = slow * 0.25f + 0.2f;
                    else if (i == 5 || i == 12 || i == 7 || i == 8 || i == 9 || i == 14 || i == 15 || i == 16)
                        fade = mid;
                    else
                        fade = slow * 0.3f + 0.3f;
                    Batch.AddSprite(GetCasterBonePosition(SpriteBones[i]), 1.5f, new Vector3(fade));
                }
                Batch.End();
            }

            if (_kind == Kind.Defense)
            {
                float ownerX = Caster.Angle.X;
                float ownerY = Caster.Angle.Y;
                if (!_stopped && _windRenderer != null)
                {
                    float fade = (float)(Math.Sin(worldTime * 0.001) + 1.0) * 0.25f + 0.2f;
                    var angle = new Vector3(ownerX, ownerY, MathHelper.ToRadians((float)(-(long)worldTime * 0.3 % 360.0)));
                    _windRenderer.DrawInstance(owner, angle, 1f, WindLight * fade * 6f, 1f);
                }

                if (_mirrorLive && _mirrorRenderer != null)
                {
                    var angle = new Vector3(ownerX, ownerY, MathHelper.ToRadians((float)((long)worldTime * 0.4 % 360.0)));
                    _mirrorRenderer.DrawInstance(owner, angle, _mirrorScale, _mirrorLight * 6f, 1f);
                }
            }

            if (_smokeCount > 0 && Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                var smokeLight = new Vector3(1f, 0.12f, 0f);
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Smoke s = ref _smokes[i];
                    if (s.Live)
                        Batch.AddSprite(s.Position, s.Scale, smokeLight * s.Alpha, s.Rotation);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _windRenderer?.Dispose();
            _mirrorRenderer?.Dispose();
        }
    }
}
