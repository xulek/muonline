#nullable enable
using System;
using System.Threading.Tasks;
using Client.Data.ATT;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Earthshake (AT_SKILL_DARK_HORSE = 62, masters AT_SKILL_ASHAKE_UP..+4), SourceMain5.2:
    /// packet  WSclient.cpp:4939       PLAYER_ATTACK_DARKHORSE, SOUND_EARTH_QUAKE (packet handler)
    /// horse   GOBoid.cpp:734          RenderDarkHorseSkill every rendered frame while the horse plays action 3
    ///         (action 6 in the 1.20.61 DarkHorse.bmd, VehicleObject.DarkHorseSkillAction):
    ///         WeaponLevel++; BITMAP_SHOCK_WAVE every 400 ms; AnimationFrame 8..9.5: rand_fps_check(2) -> 6 x
    ///         MODEL_GROUND_STONE(2) at radius 150 * (WeaponLevel / 2) * F; WeaponLevel == (BYTE)(19 / F):
    ///         MODEL_SKILL_FURY_STRIKE Kind 2 (FuryStrikeEffect kind 2)
    /// shock   ZzzEffect.cpp:3640 (sub 0: LifeTime 30, Scale 20), 13332 (Scale -= F), 19397 (ShockWave decal)
    /// stone   ZzzEffect.cpp:3582 (GroundStone: LifeTime 40, Scale 1.2 + rand%30/100, Velocity 0.3, no loop),
    ///         13266 (LifeTime 33..36: BITMAP_SMOKE 11 + MODEL_STONE sub 10, LifeTime &lt; 8: Alpha / 1.3^F, BlendMeshLight / 2^F)
    /// Not ported: AddTerrainLight (0.79,0.72,0.49). When the client does not play the horse action, the rider's PLAYER_ATTACK_DARKHORSE frame is used.
    /// </summary>
    internal sealed class DarkHorseEarthquakeEffect : SourceSkillEffectBase
    {
        private const string ShockTexturePath = "Effect/ShockWave.jpg";  // BITMAP_SHOCK_WAVE
        private const string SmokeTexturePath = "Effect/smoke01.jpg";    // BITMAP_SMOKE
        private static readonly string[] GroundStonePaths = { "Skill/GroundStone.bmd", "Skill/GroundStone2.bmd" };
        private const double WaveIntervalMs = 10 * (1000.0 / 25.0);
        private const float ModelVelocity = 0.3f;

        private struct Wave
        {
            public bool Live;
            public Vector3 Position;
            public float Angle;
            public float Scale;
            public float LifeTime;
        }

        private struct GroundStone
        {
            public bool Live;
            public int Model;
            public Vector3 Position;
            public float Angle;
            public float Scale;
            public float Alpha;
            public float BlendMeshLight;
            public float Frame;
            public float LifeTime;
        }

        private struct Smoke
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float LifeTime;
        }

        private readonly Wave[] _waves = new Wave[16];
        private readonly GroundStone[] _groundStones = new GroundStone[96];
        private readonly Smoke[] _smokes = new Smoke[192];
        private readonly SourceStonePool _stones = new(96, 1);
        private readonly SourceModelRenderer?[] _groundRenderers = new SourceModelRenderer?[2];
        private Texture2D? _shockTexture;
        private Texture2D? _smokeTexture;
        // o->WeaponLevel counts rendered frames of action 3; at 25 Hz a frame is a tick, so it is kept in ticks here
        // (the frame count times FPS_ANIMATION_FACTOR the original multiplies the radius with).
        private float _weaponLevelTicks;
        private bool _furyStarted;
        private double _lastWaveMs = double.MinValue;
        private bool _active = true;
        private static readonly Vector3 QuakeColor = new(0.79f, 0.72f, 0.49f); // AddTerrainLight colour
        private static readonly Vector3 BurstColor = new(1f, 0.7f, 0.4f);
        private float _glow;
        private float _burst;
        private float _waveAge = 100f;

        public DarkHorseEarthquakeEffect(WalkerObject caster)
            : base(caster, 0f, 0f, 512)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _shockTexture = await LoadTexture(ShockTexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            for (int i = 0; i < 2; i++)
            {
                var renderer = await LoadModel(GroundStonePaths[i], -1);
                if (renderer == null)
                    continue;
                renderer.AnimationSpeed = 0f;
                renderer.HoldLastFrame = true;
                renderer.UseNormalBlend();
                _groundRenderers[i] = renderer;
            }
            await _stones.LoadAsync(World);
        }

        protected override void OnStart()
        {
        }

        private bool TryGetHorseFrame(out float frame)
        {
            frame = 0f;
            if (Caster is not PlayerObject player)
                return false;

            if (player.Vehicle is { } horse && !horse.Hidden && horse.CurrentAction == horse.DarkHorseSkillAction)
            {
                frame = (float)horse.AnimationFramePosition;
                return true;
            }

            if (player.CurrentAction == Client.Main.Models.PlayerAction.PlayerAttackDarkhorse)
            {
                frame = (float)player.AnimationFramePosition;
                return true;
            }
            return false;
        }

        protected override void OnTick(float factor)
        {
            if (_active)
            {
                if (TryGetHorseFrame(out float frame))
                    RenderDarkHorseSkill(frame, factor);
                else if (_weaponLevelTicks > 0f || _furyStarted)
                    _active = false; // the horse left action 3
            }

            MoveWaves(factor);
            MoveGroundStones(factor);
            MoveSmokes(factor);
            _stones.Move(factor, World?.Terrain);
            UpdateLights(factor);
        }

        private void UpdateLights(float factor)
        {
            // slot 0: sustained ground glow under the horse, pulsing with every shock wave;
            // slot 1: burst while the ground stones erupt.
            _glow = MathHelper.Clamp(_glow + (_active ? 0.15f : -0.08f) * factor, 0f, 1f);
            _waveAge += factor;
            _burst = MathF.Max(0f, _burst - 0.12f * factor);
            Vector3 center = CasterPosition + Vector3.UnitZ * 60f;
            float pulse = 0.7f + 0.5f * EffectLights.Flash(_waveAge, 10f, 1.5f);
            Lights.Set(0, center, QuakeColor, 380f, 0.9f * _glow * pulse);
            Lights.Set(1, center, BurstColor, 500f, 1.5f * _burst * _burst * EffectLights.Flicker(_waveAge * 0.04f, 1.7f, 0.2f));
        }

        private void RenderDarkHorseSkill(float frame, float factor)
        {
            _weaponLevelTicks += factor;
            Vector3 horse = CasterPosition;
            double now = Client.Main.Controllers.FPSCounter.Instance.WorldTime;

            if (_lastWaveMs < now - WaveIntervalMs)
            {
                SpawnWave(horse);
                _lastWaveMs = now;
                _waveAge = 0f;
            }

            if (frame >= 8f && frame <= 9.5f)
            {
                _burst = 1f;
                if (FpsCheck(2, factor))
                {
                    // Vector(0, 150 * (WeaponLevel / 2) * FPS_ANIMATION_FACTOR, 0): integer half of the 25 Hz count.
                    // After the Fury Strike reset (WeaponLevel = -3) the window starts a few ticks later: 0..450.
                    int level = (int)_weaponLevelTicks;
                    if (level < 0)
                        level += 256; // BYTE
                    var p = new Vector3(0f, 150f * (level / 2), 0f);
                    float angleZ = MuGame.Random.Next(360);
                    for (int i = 0; i < 6; i++)
                    {
                        angleZ += 60f;
                        SpawnGroundStone(horse + SourceJointMath.Rotate(p, new Vector3(0f, 0f, angleZ)));
                    }
                }
                Client.Main.Graphics.Camera.Instance.SetEarthQuake((MuGame.Random.Next(3) - 3) * 0.7f); // GOBoid.cpp:767
            }
            else if (!_furyStarted && _weaponLevelTicks >= 19f)
            {
                // WeaponLevel == (BYTE)(19 / FPS_ANIMATION_FACTOR): 19 ticks; an exact frame compare misses it at a
                // varying frame rate, so the crossing is used and it fires once.
                var fury = new FuryStrikeEffect(Caster, kind: 2);
                World?.Objects.Add(fury);
                _ = fury.Load();
                _furyStarted = true;
                _weaponLevelTicks = -3f; // WeaponLevel = -3
            }
        }

        // ------------------------------------------------------------------ BITMAP_SHOCK_WAVE sub 0

        private void SpawnWave(Vector3 position)
        {
            for (int i = 0; i < _waves.Length; i++)
            {
                ref Wave w = ref _waves[i];
                if (w.Live)
                    continue;
                w = new Wave { Live = true, Position = position, Angle = CasterYawDegrees, Scale = 20f, LifeTime = 30f };
                return;
            }
        }

        private void MoveWaves(float factor)
        {
            for (int i = 0; i < _waves.Length; i++)
            {
                ref Wave w = ref _waves[i];
                if (!w.Live)
                    continue;
                w.Scale = MathF.Max(0f, w.Scale - factor);
                w.LifeTime -= factor;
                if (w.LifeTime <= 0f)
                    w.Live = false;
            }
        }

        // ------------------------------------------------------------------ MODEL_GROUND_STONE

        private void SpawnGroundStone(Vector3 position)
        {
            var terrain = World?.Terrain;
            if (terrain != null)
            {
                var flags = terrain.RequestTerrainFlag((int)(position.X / Constants.TERRAIN_SCALE), (int)(position.Y / Constants.TERRAIN_SCALE));
                if (flags.HasFlag(TWFlags.NoMove) || flags.HasFlag(TWFlags.NoGround) || flags.HasFlag(TWFlags.Water))
                    return;
            }

            for (int i = 0; i < _groundStones.Length; i++)
            {
                ref GroundStone g = ref _groundStones[i];
                if (g.Live)
                    continue;
                int model = MuGame.Random.Next(2);
                g = new GroundStone
                {
                    Live = true,
                    Model = model,
                    Position = position,
                    Angle = MuGame.Random.Next(360),
                    Scale = (model == 0 ? 1.2f : 1f) + MuGame.Random.Next(30) / 100f,
                    Alpha = 1f,
                    BlendMeshLight = 1f,
                    LifeTime = 40f,
                };
                return;
            }
        }

        private void MoveGroundStones(float factor)
        {
            float fade = MathF.Pow(1f / 1.3f, factor);
            float bmlFade = MathF.Pow(0.5f, factor);
            for (int i = 0; i < _groundStones.Length; i++)
            {
                ref GroundStone g = ref _groundStones[i];
                if (!g.Live)
                    continue;

                if (g.LifeTime > 32f && g.LifeTime < 37f)
                {
                    Vector3 position = g.Position + new Vector3(60f, -60f, 50f);
                    if (FpsCheck(1, factor))
                        SpawnSmoke(position, new Vector3(1f, 0.8f, 0.6f), 2f);
                    if (FpsCheck(1, factor))
                        _stones.Spawn(position, new Vector3(0f, 0f, g.Angle), Vector3.One, 10);
                }
                if (g.LifeTime < 8f)
                {
                    g.Alpha *= fade;
                    g.BlendMeshLight *= bmlFade;
                }

                g.Frame += ModelVelocity * factor;
                g.LifeTime -= factor;
                if (g.LifeTime <= 0f)
                    g.Live = false;
            }
        }

        // ------------------------------------------------------------------ BITMAP_SMOKE sub 11

        private void SpawnSmoke(Vector3 position, Vector3 light, float scale)
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke s = ref _smokes[i];
                if (s.Live)
                    continue;
                s = new Smoke
                {
                    Live = true,
                    Position = position + new Vector3((MuGame.Random.Next(64) - 32) * factor, (MuGame.Random.Next(64) - 32) * factor, (MuGame.Random.Next(64) + 32) * factor),
                    Angle = new Vector3(MuGame.Random.Next(90) - 45, 0f, MuGame.Random.Next(360)),
                    Velocity = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f),
                    Light = light,
                    Scale = scale,
                    Rotation = MuGame.Random.Next(360),
                    LifeTime = 50f,
                };
                return;
            }
        }

        private void MoveSmokes(float factor)
        {
            for (int i = 0; i < _smokes.Length; i++)
            {
                ref Smoke s = ref _smokes[i];
                if (!s.Live)
                    continue;
                s.LifeTime -= factor;
                if (s.LifeTime <= 0f) { s.Live = false; continue; }
                s.Position += SourceJointMath.Rotate(s.Velocity, s.Angle) * factor;
                s.Velocity *= 0.4f; // per frame
                s.Scale += factor * 0.05f;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_active || _stones.HasLive)
                return true;
            for (int i = 0; i < _waves.Length; i++) if (_waves[i].Live) return true;
            for (int i = 0; i < _groundStones.Length; i++) if (_groundStones[i].Live) return true;
            for (int i = 0; i < _smokes.Length; i++) if (_smokes[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;

            if (Batch.Begin(_shockTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _waves.Length; i++)
                {
                    ref Wave w = ref _waves[i];
                    if (w.Live)
                        Batch.AddTerrainDecal(terrain, w.Position.X, w.Position.Y, w.Scale, w.Scale, Vector3.One, -w.Angle);
                }
                Batch.End();
            }

            for (int m = 0; m < 2; m++)
            {
                SourceModelRenderer? renderer = _groundRenderers[m];
                if (renderer == null)
                    continue;
                for (int i = 0; i < _groundStones.Length; i++)
                {
                    ref GroundStone g = ref _groundStones[i];
                    if (!g.Live || g.Model != m)
                        continue;
                    renderer.SetAnimationTime(g.Frame);
                    renderer.Update(LastGameTime);
                    Vector3 light = terrain?.EvaluateTerrainLight(g.Position.X, g.Position.Y) ?? Vector3.One;
                    float bml = MathF.Round(g.BlendMeshLight * 50f) / 50f; // quantized: BML is baked into vertex colours
                    renderer.DrawInstance(g.Position, new Vector3(0f, 0f, MathHelper.ToRadians(g.Angle)), g.Scale, light, bml,
                        MathF.Max(g.Alpha, 0.003f));
                }
            }

            _stones.DrawModels(LastGameTime, terrain);

            if (Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smokes.Length; i++)
                {
                    ref Smoke s = ref _smokes[i];
                    if (s.Live)
                        Batch.AddSprite(s.Position, s.Scale, s.Light * (s.LifeTime / 50f), s.Rotation);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _groundRenderers[0]?.Dispose();
            _groundRenderers[1]?.Dispose();
            _stones.Dispose();
        }
    }
}
