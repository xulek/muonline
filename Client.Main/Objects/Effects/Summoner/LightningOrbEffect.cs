#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Summoner
{
    /// <summary>
    /// Lightning Orb (AT_SKILL_ALICE_LIGHTNINGORB = 216), SourceMain5.2:
    /// hands   ZzzCharacter.cpp:10386 while PLAYER_SKILL_LIGHTNING_ORB(_UNI/_DINO/_FENRIR): 2 x MODEL_FENRIR_THUNDER sub 2 at bone 27 (0.2, 0.2, 1)
    /// gate    AttackStage default (ZzzCharacter.cpp:2876): action outside FIST..RIDE_SKILL -> 14 ticks after AttackTime = 1
    /// release ZzzCharacter.cpp:5011 (needs TargetCharacter): CreateEffect(MODEL_LIGHTNING_ORB, o->Position, o->Angle, (1,1,1), 0, to),
    ///         SOUND_SUMMON_SKILL_LIGHTORB
    /// init    ZzzEffect.cpp:860  sub 0: LifeTime 20, Direction (0,-60,0), Z += 100*F; sub 1: LifeTime 18
    /// move    ZzzEffect.cpp:8821 fRot = WorldTime*0.0006*360
    ///         sub 0: sprites SHINY+1 4/3 (0.1,0.7,1.5) +-fRot, MAGIC 1/0.5 (0.1,0.1,1.5) +-fRot, PIN_LIGHT 2 x2 (0.5,0.5,1.5) rand;
    ///                particles MAGIC 0 (0.4,0.4,1.5) 1, 3 x SPARK+1 13; CheckTargetRange (245/290): owner within 100 XY ->
    ///                LifeTime 1 + CreateEffectFpsChecked(MODEL_LIGHTNING_ORB sub 1); epilogue 17884: Position += Rotate(Direction, Angle)*F
    ///         sub 1: LifeTime &gt;= 5: SHINY+5 3/2 (0.1,0.5,1.5), PIN_LIGHT 4 x2 (L0*.3, L1*.3, L2), ENERGY 4 (Light, fRot);
    ///                &gt;= 15: 5 x SPARK+1 20; &gt;= 14: 2 x SHOCK_WAVE 0 (0.4,0.3,1) 0.3; 2 x FENRIR_THUNDER 3 (0.2,0.2,1);
    ///                &lt;= 5: 2 x SMOKE 40; Light *= (1/1.08)^F
    /// particles ZzzEffectParticle.cpp MAGIC 767/5018, SPARK+1 13 2209/6737, 20 2305/6840, SHOCK_WAVE 3762/8725, SMOKE 40 1419/5591;
    ///         all default RenderSprite, additive (JPG)
    /// </summary>
    internal sealed class LightningOrbEffect : SourceSkillEffectBase
    {
        private const string SoundPath = "Sound/SE_Ch_summoner_skill01_lightningof.wav";
        private const string Shiny1Path = "Effect/Shiny02.jpg";       // BITMAP_SHINY + 1
        private const string Shiny5Path = "Effect/Shiny05.jpg";       // BITMAP_SHINY + 5 (second LoadBitmap wins)
        private const string MagicPath = "Effect/Magic_Ground1.jpg";  // BITMAP_MAGIC
        private const string PinLightPath = "Effect/pin_lights.jpg";  // BITMAP_PIN_LIGHT
        private const string EnergyPath = "Effect/Thunder01.jpg";     // BITMAP_ENERGY
        private const string SparkPath = "Effect/Spark03.jpg";        // BITMAP_SPARK + 1
        private const string ShockPath = "Effect/ShockWave.jpg";      // BITMAP_SHOCK_WAVE
        private const string SmokePath = "Effect/smoke01.jpg";        // BITMAP_SMOKE

        private const float LimitAttackTime = 15f;
        private const int MaxOrbs = 4;
        private const int MaxParticles = 384;
        private const int MaxSprites = 64;
        private const int HandBone = 27;
        private static readonly Vector3 ThunderLight = new(0.2f, 0.2f, 1f);

        private enum Tex { Shiny1, Shiny5, Magic, PinLight, Energy, Spark, Shock, Smoke, Count }

        private enum Kind : byte { Magic, Spark13, Spark20, Shock, Smoke40 }

        private struct Orb
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle; // degrees
            public Vector3 Light;
            public float LifeTime;
        }

        private struct Particle
        {
            public bool Live;
            public Kind Kind;
            public Vector3 Position;
            public Vector3 Velocity; // world space (MovePosition rotation applied at spawn)
            public Vector3 SmokeLocal;
            public Vector3 SmokeAngle;
            public Vector3 Light;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private struct Sprite
        {
            public Tex Tex;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
        }

        private readonly WalkerObject? _target;
        private readonly Orb[] _orbs = new Orb[MaxOrbs];
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private readonly Sprite[] _sprites = new Sprite[MaxSprites];
        private readonly Texture2D?[] _textures = new Texture2D?[(int)Tex.Count];
        private readonly SourceFenrirThunderPool _thunders = SourceFenrirThunderPool.Shared;
        private SourceModelRenderer? _thunderModel;
        private Texture2D? _lightTexture;
        private int _spriteCount;
        private int _particleCount;
        private float _attackTime = 1f;
        private bool _released;
        private bool _casting = true;

        public LightningOrbEffect(WalkerObject caster, WalkerObject? target)
            : base(caster, 0f, 0f, 1024)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _textures[(int)Tex.Shiny1] = await LoadTexture(Shiny1Path);
            _textures[(int)Tex.Shiny5] = await LoadTexture(Shiny5Path);
            _textures[(int)Tex.Magic] = await LoadTexture(MagicPath);
            _textures[(int)Tex.PinLight] = await LoadTexture(PinLightPath);
            _textures[(int)Tex.Energy] = await LoadTexture(EnergyPath);
            _textures[(int)Tex.Spark] = await LoadTexture(SparkPath);
            _textures[(int)Tex.Shock] = await LoadTexture(ShockPath);
            _textures[(int)Tex.Smoke] = await LoadTexture(SmokePath);
            _lightTexture = await LoadTexture(SourceFenrirThunderPool.LightTexturePath);
            _thunderModel = await LoadModel(SourceFenrirThunderPool.ModelPath, -1);
            if (_thunderModel != null)
            {
                _thunderModel.UseNormalBlend();
                _thunderModel.AnimationSpeed = 0f;
            }
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            _thunders.Move(this, factor);
            _spriteCount = 0;

            if (_casting)
            {
                _casting = Caster is PlayerObject player && IsOrbAction(player.CurrentAction);
                if (_casting)
                {
                    Vector3 forearm = GetCasterBonePosition(HandBone);
                    _thunders.Spawn(this, 2, forearm, ThunderLight, factor);
                    _thunders.Spawn(this, 2, forearm, ThunderLight, factor);
                }
            }

            if (!_released)
            {
                _attackTime += factor;
                if ((int)_attackTime >= LimitAttackTime)
                {
                    _released = true;
                    if (IsPresent(_target))
                    {
                        Vector3 position = CasterPosition;
                        position.Z += 100f * factor;
                        SpawnOrb(0, position, ToDegrees(Caster.Angle), Vector3.One);
                        SoundController.Instance.PlayBuffer(SoundPath);
                    }
                }
            }

            MoveParticles(factor);

            float rot = (float)(FPSCounter.Instance.WorldTime * 0.0006 * 360.0);
            for (int i = 0; i < _orbs.Length; i++)
            {
                ref Orb o = ref _orbs[i];
                if (!o.Live)
                    continue;

                if (o.SubType == 0)
                    MoveFlight(ref o, rot, factor);
                else
                    MoveBurst(ref o, rot, factor);

                o.LifeTime -= factor;
                if (o.LifeTime <= 0f)
                    o.Live = false;
            }
        }

        private void MoveFlight(ref Orb o, float rot, float factor)
        {
            AddSprite(Tex.Shiny1, o.Position, 4f, new Vector3(0.1f, 0.7f, 1.5f), rot);
            AddSprite(Tex.Shiny1, o.Position, 3f, new Vector3(0.1f, 0.7f, 1.5f), -rot);
            AddSprite(Tex.Magic, o.Position, 1f, new Vector3(0.1f, 0.1f, 1.5f), rot);
            AddSprite(Tex.Magic, o.Position, 0.5f, new Vector3(0.1f, 0.1f, 1.5f), -rot);
            AddSprite(Tex.PinLight, o.Position, 2f, new Vector3(0.5f, 0.5f, 1.5f), MuGame.Random.Next(360));
            AddSprite(Tex.PinLight, o.Position, 2f, new Vector3(0.5f, 0.5f, 1.5f), MuGame.Random.Next(360));

            if (FPSCounter.Instance.RandFPSCheck(1))
                SpawnMagic(o.Position, new Vector3(0.4f, 0.4f, 1.5f));
            for (int i = 0; i < 3; i++)
                if (FPSCounter.Instance.RandFPSCheck(1))
                    SpawnSpark13(o.Position, o.Angle, o.Light);

            // CheckTargetRange: Owner Live && Visible, XY range <= 100.
            if (IsPresent(_target) && _target!.Visible)
            {
                Vector3 to = _target.WorldPosition.Translation;
                float dx = o.Position.X - to.X;
                float dy = o.Position.Y - to.Y;
                if (MathF.Sqrt(dx * dx + dy * dy) <= 100f)
                {
                    o.LifeTime = 1f;
                    if (FPSCounter.Instance.RandFPSCheck(1))
                        SpawnOrb(1, o.Position, o.Angle, o.Light);
                }
            }

            // Epilogue MoveParticle(o, true): Direction (0, -60, 0) rotated by Angle.
            o.Position += SourceJointMath.Rotate(new Vector3(0f, -60f, 0f), o.Angle) * factor;
        }

        private void MoveBurst(ref Orb o, float rot, float factor)
        {
            if (o.LifeTime >= 5f)
            {
                AddSprite(Tex.Shiny5, o.Position, 3f, new Vector3(0.1f, 0.5f, 1.5f), rot);
                AddSprite(Tex.Shiny5, o.Position, 2f, new Vector3(0.1f, 0.5f, 1.5f), -rot);
                var pin = new Vector3(o.Light.X * 0.3f, o.Light.Y * 0.3f, o.Light.Z * 1f);
                AddSprite(Tex.PinLight, o.Position, 4f, pin, MuGame.Random.Next(360));
                AddSprite(Tex.PinLight, o.Position, 4f, pin, MuGame.Random.Next(360));
                AddSprite(Tex.Energy, o.Position, 4f, o.Light, rot);
            }

            if (o.LifeTime >= 15f)
                for (int i = 0; i < 5; i++)
                    if (FPSCounter.Instance.RandFPSCheck(1))
                        SpawnSpark20(o.Position, o.Angle, o.Light);

            if (o.LifeTime >= 14f)
                for (int i = 0; i < 2; i++)
                    if (FPSCounter.Instance.RandFPSCheck(1))
                        SpawnShock(o.Position, new Vector3(0.4f, 0.3f, 1f));

            for (int i = 0; i < 2; i++)
                if (FPSCounter.Instance.RandFPSCheck(1))
                    _thunders.Spawn(this, 3, o.Position, ThunderLight, factor);

            if (o.LifeTime <= 5f)
                for (int i = 0; i < 2; i++)
                    if (FPSCounter.Instance.RandFPSCheck(1))
                        SpawnSmoke40(o.Position, o.Angle, o.Light, factor);

            o.Light *= MathF.Pow(1f / 1.08f, factor);
        }

        private void SpawnOrb(int subType, Vector3 position, Vector3 angle, Vector3 light)
        {
            for (int i = 0; i < _orbs.Length; i++)
            {
                if (_orbs[i].Live)
                    continue;
                _orbs[i] = new Orb { Live = true, SubType = subType, Position = position, Angle = angle, Light = light, LifeTime = subType == 0 ? 20f : 18f };
                return;
            }
        }

        private void AddSprite(Tex tex, Vector3 position, float scale, Vector3 light, float rotation)
        {
            if (_spriteCount >= _sprites.Length)
                return;
            _sprites[_spriteCount++] = new Sprite { Tex = tex, Position = position, Scale = scale, Light = light, Rotation = rotation };
        }

        // ------------------------------------------------------------------ particles

        private ref Particle Allocate(Kind kind, Vector3 position, Vector3 light, out bool ok)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;
                p = default;
                p.Live = true;
                p.Kind = kind;
                p.Position = position;
                p.Light = light;
                _particleCount++;
                ok = true;
                return ref p;
            }
            ok = false;
            return ref _particles[0];
        }

        private void SpawnMagic(Vector3 position, Vector3 light)
        {
            ref Particle p = ref Allocate(Kind.Magic, position, light, out bool ok);
            if (!ok) return;
            p.LifeTime = 10f;
            p.Scale = 1f;
        }

        private void SpawnSpark13(Vector3 position, Vector3 angle, Vector3 light)
        {
            ref Particle p = ref Allocate(Kind.Spark13, position, light, out bool ok);
            if (!ok) return;
            var local = new Vector3(-4f + MuGame.Random.Next(8), MuGame.Random.Next(4), MuGame.Random.Next(4));
            p.LifeTime = 30f;
            p.Scale = MuGame.Random.Next(5) / 10f + 1f;
            p.Velocity = SourceJointMath.Rotate(local, angle);
        }

        private void SpawnSpark20(Vector3 position, Vector3 angle, Vector3 light)
        {
            ref Particle p = ref Allocate(Kind.Spark20, position, light, out bool ok);
            if (!ok) return;
            p.Scale = 1f + MuGame.Random.Next(10) * 0.02f;
            p.LifeTime = MuGame.Random.Next(10) + 60;
            angle.Z = MuGame.Random.Next(360);
            p.Gravity = MuGame.Random.Next(10) + 15;
            var local = new Vector3((MuGame.Random.Next(40) - 20) * 0.1f * 2.5f, (MuGame.Random.Next(40) - 20) * 0.1f * 2.5f, 0f);
            Vector3 velocity = SourceJointMath.Rotate(local, angle);
            p.Velocity = SourceJointMath.Rotate(velocity, angle); // MovePosition rotates the stored Velocity again
        }

        private void SpawnShock(Vector3 position, Vector3 light)
        {
            ref Particle p = ref Allocate(Kind.Shock, position, light, out bool ok);
            if (!ok) return;
            p.LifeTime = 7f;
            p.Scale = 0.3f;
        }

        private void SpawnSmoke40(Vector3 position, Vector3 angle, Vector3 light, float factor)
        {
            ref Particle p = ref Allocate(Kind.Smoke40, position, light, out bool ok);
            if (!ok) return;
            p.LifeTime = 50f;
            p.Scale = (MuGame.Random.Next(32) + 80) * 0.01f;
            p.Position.X += (MuGame.Random.Next(64) - 32) * factor;
            p.Position.Y += (MuGame.Random.Next(64) - 32) * factor;
            p.Position.Z += (MuGame.Random.Next(64) + 32) * factor;
            angle.X = MuGame.Random.Next(90) - 45;
            angle.Z = MuGame.Random.Next(360);
            p.SmokeAngle = angle;
            p.SmokeLocal = new Vector3(0f, -(MuGame.Random.Next(8) + 40), 0f);
        }

        private void MoveParticles(float factor)
        {
            if (_particleCount == 0)
                return;

            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    Kill(ref p);
                    continue;
                }

                switch (p.Kind)
                {
                    case Kind.Magic:
                        p.Scale -= 0.05f * factor;
                        p.Light -= new Vector3(0.01f * factor);
                        break;
                    case Kind.Spark13:
                        p.Position += p.Velocity * factor;
                        p.Light *= MathF.Pow(1f / 1.05f, factor);
                        p.Scale -= factor * 0.04f;
                        break;
                    case Kind.Spark20:
                    {
                        p.Position += p.Velocity * factor;
                        p.Light *= MathF.Pow(1f / 1.02f, factor);
                        if (p.Light.X <= 0.05f)
                        {
                            Kill(ref p);
                            continue;
                        }
                        p.Position.Z += p.Gravity * 0.5f * factor;
                        p.Gravity -= 1.5f * factor;
                        float height = TerrainHeight(p.Position);
                        if (p.Position.Z < height + 3f)
                        {
                            p.Position.Z = height + 3f;
                            p.Gravity = -p.Gravity * 0.3f;
                            p.LifeTime -= 2f * factor;
                        }
                        break;
                    }
                    case Kind.Shock:
                        p.Light *= MathF.Pow(1f / 1.5f, factor);
                        p.Scale += factor * 0.8f;
                        break;
                    case Kind.Smoke40:
                    {
                        p.Position += SourceJointMath.Rotate(p.SmokeLocal, p.SmokeAngle) * factor;
                        float luminosity = p.LifeTime / 50f;
                        p.Light = new Vector3(luminosity * 0.5f, luminosity * 0.5f, luminosity * 1f);
                        p.SmokeLocal *= 0.4f; // VectorScale per frame, not factor-scaled
                        p.Scale += factor * 0.05f;
                        break;
                    }
                }
            }
        }

        private void Kill(ref Particle p)
        {
            p.Live = false;
            _particleCount--;
        }

        // ------------------------------------------------------------------ draw

        protected override bool HasActiveContent()
        {
            if (_casting || !_released || _particleCount > 0 || _thunders.HasLive(this))
                return true;
            for (int i = 0; i < _orbs.Length; i++)
                if (_orbs[i].Live)
                    return true;
            return false;
        }

        protected override void OnDraw()
        {
            _thunders.DrawModels(this, _thunderModel, World?.Terrain);

            for (int t = 0; t < (int)Tex.Count; t++)
            {
                if (!HasSprites((Tex)t))
                    continue;
                if (!Batch.Begin(_textures[t], Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                    continue;
                for (int i = 0; i < _spriteCount; i++)
                {
                    ref Sprite s = ref _sprites[i];
                    if (s.Tex == (Tex)t)
                        Batch.AddSprite(s.Position, s.Scale, s.Light, s.Rotation);
                }
                Batch.End();
            }

            _thunders.DrawSprites(this, Batch, _lightTexture);

            if (_particleCount == 0)
                return;
            DrawParticles(Kind.Magic, Tex.Magic);
            DrawParticles(Kind.Spark13, Tex.Spark);
            DrawParticles(Kind.Spark20, Tex.Spark);
            DrawParticles(Kind.Shock, Tex.Shock);
            DrawParticles(Kind.Smoke40, Tex.Smoke);
        }

        private bool HasSprites(Tex tex)
        {
            for (int i = 0; i < _spriteCount; i++)
                if (_sprites[i].Tex == tex)
                    return true;
            return false;
        }

        private void DrawParticles(Kind kind, Tex tex)
        {
            if (!Batch.Begin(_textures[(int)tex], Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live && p.Kind == kind)
                    Batch.AddSprite(p.Position, p.Scale, p.Light, 0f);
            }
            Batch.End();
        }

        private static bool IsOrbAction(PlayerAction action)
            => action is PlayerAction.PlayerSkillLightningOrb or PlayerAction.PlayerSkillLightningOrbUni
                or PlayerAction.PlayerSkillLightningOrbDino or PlayerAction.PlayerSkillLightningOrbFenrir;

        private static Vector3 ToDegrees(Vector3 angle)
            => new(MathHelper.ToDegrees(angle.X), MathHelper.ToDegrees(angle.Y), MathHelper.ToDegrees(angle.Z));

        private static bool IsPresent(WalkerObject? walker)
            => walker != null && walker.Status != GameControlStatus.Disposed && walker.World != null;

        protected override void DisposeEffect() => _thunders.Release(this);
    }
}
