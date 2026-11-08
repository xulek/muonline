using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Per-item sprite and particle effects that SourceMain5.2 emits from RenderLinkObject
    /// (ZzzCharacter.cpp) while an item is attached to a character.
    /// <para>
    /// Sprites follow CreateSprite/RenderSprite: they live for one frame, are drawn additively
    /// (glBlendFunc(GL_ONE, GL_ONE)) and are <c>texture width * Scale</c> world units wide.
    /// Particles follow CreateParticle/MoveParticles and are simulated at the original 25 Hz.
    /// </para>
    /// </summary>
    public sealed class ItemLinkEffect : ItemEffect
    {
        /// <summary>SourceMain BITMAP_* sprite textures used by item effects.</summary>
        public enum Fx
        {
            Light,        // BITMAP_LIGHT      Effect/flare01
            Light1,       // BITMAP_LIGHT + 1  Object9/Impack03
            Light3,       // BITMAP_LIGHT + 3  Effect/Impack01
            Shiny1,       // BITMAP_SHINY + 1  Effect/Shiny02
            Shiny2,       // BITMAP_SHINY + 2  Effect/Shiny03
            Shiny6,       // BITMAP_SHINY + 6  Effect/shiny05
            Spark,        // BITMAP_SPARK      Effect/Spark02
            Spark1,       // BITMAP_SPARK + 1  Effect/Spark03
            Magic,        // BITMAP_MAGIC      Effect/Magic_Ground1
            Flare,        // BITMAP_FLARE      Effect/Flare
            FlareRed,     // BITMAP_FLARE_RED  Effect/flareRed
            FlareBlue,    // BITMAP_FLARE_BLUE Effect/flareBlue
            PinLight,     // BITMAP_PIN_LIGHT  Effect/pin_lights
            Waterfall2,   // BITMAP_WATERFALL_2
            Waterfall4,   // BITMAP_WATERFALL_4
            LightMarks,   // BITMAP_LIGHT_MARKS Effect/lightmarks
            ShockWave,    // BITMAP_SHOCK_WAVE Effect/ShockWave
            Mega1,        // BITMAP_LIGHTNING_MEGA1
            Mega2,
            Mega3,
            JointLaser,   // BITMAP_JOINT_ENERGY Effect/JointLaser01
            JointEnergy,  // BITMAP_JOINT_HEALING Effect/JointEnergy01
            Shiny0,       // BITMAP_SHINY      Effect/Shiny01
            Clud64,       // BITMAP_CLUD64     Effect/Clud64
            Energy2,      // BITMAP_CHROME_ENERGY2 Effect/energy02
            JointThunder, // BITMAP_JOINT_THUNDER Effect/JointThunder01
            JointSpirit,  // BITMAP_JOINT_SPIRIT Effect/JointSpirit01
            Count,
        }

        private static readonly string[] TexturePaths =
        {
            "Effect/flare01.jpg",
            "Object9/Impack03.jpg",
            "Effect/Impack01.jpg",
            "Effect/Shiny02.jpg",
            "Effect/Shiny03.jpg",
            "Effect/Shiny05.jpg",
            "Effect/Spark02.jpg",
            "Effect/Spark03.jpg",
            "Effect/Magic_Ground1.jpg",
            "Effect/Flare.jpg",
            "Effect/flareRed.jpg",
            "Effect/flareBlue.jpg",
            "Effect/pin_lights.jpg",
            "Effect/WATERFALL2.jpg",
            "Effect/WATERFALL4.jpg",
            "Effect/lightmarks.jpg",
            "Effect/ShockWave.jpg",
            "Effect/lighting_mega01.jpg",
            "Effect/lighting_mega02.jpg",
            "Effect/lighting_mega03.jpg",
            "Effect/JointLaser01.jpg",
            "Effect/JointEnergy01.jpg",
            "Effect/Shiny01.jpg",
            "Effect/Clud64.jpg",
            "Effect/energy02.jpg",
            "Effect/JointThunder01.jpg",
            "Effect/JointSpirit01.jpg",
        };

        private static readonly Texture2D[] s_textures = new Texture2D[(int)Fx.Count];
        private static Task s_textureLoad;

        private const float TickSeconds = 1f / 25f;
        private const int MaxTicksPerFrame = 5;
        // DrawAfter keeps the used textures in a 32-bit mask; this fails to compile when Fx outgrows it.
        private const uint FxTextureMaskHeadroom = 32 - (uint)Fx.Count;
        private const int MaxSprites = 256;
        private const int MaxParticles = 160;

        private struct SpriteRequest
        {
            public Fx Texture;
            public Vector3 Position;
            public float Scale;
            public Vector3 Color;
            public float RotationDegrees;
            public bool Subtract; // CreateSprite SubType 1: EnableAlphaBlendMinus
        }

        private struct Beam
        {
            public Fx Texture;
            public Vector3 From, To;
            public float Width;
            public Vector3 Color;
        }

        private readonly List<Beam> _beams = new(16);

        private static readonly BlendState s_minusBlend = new()
        {
            ColorSourceBlend = Blend.Zero,
            ColorDestinationBlend = Blend.InverseSourceColor,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.One,
        };

        private struct Particle
        {
            public bool Live;
            public Fx Texture;
            public int Kind;           // 0 = BITMAP_SPARK (gravity), 11/14 = BITMAP_SPARK+1 subtypes, 100 = MODEL_STAR_SHINE
            public float Rotation;
            public Vector3 Position;
            public Vector3 Velocity;   // world units per 25 Hz tick
            public Vector3 Color;
            public float Scale;
            public float ScaleDecay;
            public float ColorDecay;
            public float Gravity;
            public float LifeTicks;
        }

        private const int TrailSlots = 2;
        private const int TrailPoints = 12; // JOINT LifeTime 12 ticks

        private sealed class Trail
        {
            public readonly Vector3[] Points = new Vector3[TrailPoints];
            public int Count;
            public Vector3 Color;
            public float Width;
            public bool TouchedThisFrame;
            public Vector3 Latest;
        }

        private readonly Trail[] _trails = { new Trail(), new Trail() };

        // BITMAP_FLARE joint subtype 41: ribbon that waits 20 ticks, then rises with growing speed and fades.
        private sealed class Ribbon
        {
            public readonly Vector3[] Points = new Vector3[50];
            public int Count;
            public Vector3 Position;
            public Vector3 Color;
            public float Width;
            public float Life = 40f;
            public float VelocityZ;
        }

        private readonly List<Ribbon> _ribbons = new();

        /// <summary>CreateJoint(BITMAP_FLARE, ..., 41, NULL, Scale) at a position.</summary>
        public void SpawnRisingRibbon(Vector3 position, Vector3 color, float width)
        {
            if (_ribbons.Count >= 10)
                return;
            var ribbon = new Ribbon { Position = position, Color = color, Width = width, VelocityZ = _random.Next(5) + 5 };
            ribbon.Points[0] = position;
            ribbon.Count = 1;
            _ribbons.Add(ribbon);
        }

        private void TickRibbons()
        {
            for (int i = _ribbons.Count - 1; i >= 0; i--)
            {
                var r = _ribbons[i];
                // MoveJoint order: move, light fade, CreateTail, LifeTime--.
                if (r.Life <= 20f)
                {
                    r.VelocityZ += 1f;
                    r.Position.Z += r.VelocityZ;
                    r.Color /= 1.1f;
                }
                if (r.Life < 10f)
                    r.Color /= 1.3f;
                int count = Math.Min(r.Count + 1, r.Points.Length);
                for (int t = count - 1; t > 0; t--)
                    r.Points[t] = r.Points[t - 1];
                r.Points[0] = r.Position;
                r.Count = count;
                r.Life -= 1f;
                if (r.Life < 0f)
                    _ribbons.RemoveAt(i);
            }
        }

        private int WriteRibbon(int quad, Ribbon r, float alpha)
        {
            if (r.Count < 2)
                return quad;
            var color = new Color(Vector3.Clamp(r.Color * alpha, Vector3.Zero, Vector3.One));
            Vector3 across = Vector3.UnitX * (r.Width * 0.5f), vertical = Vector3.UnitZ * (r.Width * 0.5f);
            int capacity = _vertices.Length / 4;
            for (int i = 0; i < r.Count - 1 && quad + 2 <= capacity; i++)
            {
                Vector3 a = r.Points[i], b = r.Points[i + 1];
                // RenderJoints: U = (NumTails - j) / (MaxTails - 1); the newest tail has the largest U.
                float uA = (r.Count - 1 - i) / 49f, uB = (r.Count - 2 - i) / 49f;
                foreach (var offset in new[] { across, vertical })
                {
                    int v = quad * 4;
                    _vertices[v] = new VertexPositionColorTexture(a - offset, color, new Vector2(uA, 0f));
                    _vertices[v + 1] = new VertexPositionColorTexture(b - offset, color, new Vector2(uB, 0f));
                    _vertices[v + 2] = new VertexPositionColorTexture(b + offset, color, new Vector2(uB, 1f));
                    _vertices[v + 3] = new VertexPositionColorTexture(a + offset, color, new Vector2(uA, 1f));
                    quad++;
                }
            }
            return quad;
        }

        /// <summary>World position of the character (or of the owner when it is not a child model).</summary>
        public Vector3 CharacterPosition => (Character ?? Owner)?.WorldPosition.Translation ?? Vector3.Zero;

        // BITMAP_JOINT_HEALING subtype 9 ("Ancient blur"), positions relative to the character.
        private sealed class Helix
        {
            public const int MaxTails = 20;
            public readonly Vector3[] Tails = new Vector3[MaxTails];
            public int TailCount;
            public float MultiUse;
            public float Radius = 50f;
            public float LifeTicks = 90f;
        }

        private readonly List<Helix> _helices = new();

        /// <summary>Number of 25 Hz ticks elapsed this frame.</summary>
        public int TicksThisFrame => _ticksThisFrame;

        /// <summary>CreateJoint(BITMAP_JOINT_HEALING, Position, Position, Angle, 9, Owner, 10).</summary>
        public void SpawnAncientHelix(float angleDegrees)
        {
            if (_helices.Count < 16)
                _helices.Add(new Helix { MultiUse = angleDegrees });
        }

        private void TickHelices()
        {
            for (int i = _helices.Count - 1; i >= 0; i--)
            {
                var h = _helices[i];
                h.LifeTicks -= 1f;
                if (h.LifeTicks <= 0f)
                {
                    _helices.RemoveAt(i);
                    continue;
                }

                var local = new Vector3(
                    MathF.Sin(h.MultiUse * 0.1f) * h.Radius,
                    MathF.Cos(h.MultiUse * 0.1f) * h.Radius,
                    10f + (90f - h.LifeTicks));
                h.MultiUse += 2f;
                h.Radius = MathF.Max(0f, h.Radius - 1f);

                int count = Math.Min(h.TailCount + 1, Helix.MaxTails);
                for (int t = count - 1; t > 0; t--)
                    h.Tails[t] = h.Tails[t - 1];
                h.Tails[0] = local;
                h.TailCount = count;
            }
        }
        private readonly List<SpriteRequest> _sprites = new(64);
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private readonly Random _random = new();
        private VertexPositionColorTexture[] _vertices;

        private float _tickAccumulator;
        private int _ticksThisFrame;
        private Matrix[] _bones;
        private Matrix _ownerWorld;
        private Matrix _characterRotation = Matrix.Identity;

        /// <summary>World time in milliseconds (SourceMain WorldTime).</summary>
        public double WorldTime { get; private set; }

        /// <summary>The character carrying the item (root model), when attached to one.</summary>
        public ModelObject Character { get; private set; }

        /// <summary>
        /// Optional emitter used instead of the per-item switch, e.g. character-level effects
        /// such as SourceMain's NextGradeObjectRender (+15 equipment).
        /// </summary>
        public Action<ItemLinkEffect> CustomEmitter { get; set; }

        /// <summary>
        /// True for items that are not held by a character (drops): only the RenderPartObjectEffect
        /// effects apply, not the RenderLinkObject weapon effects.
        /// </summary>
        public bool StandaloneItem { get; set; }

        /// <summary>One frame of emission and particle ticks (run by <see cref="ItemEffectSystem"/>).</summary>
        internal override void TickEffect(GameTime gameTime)
        {
            if (IsDisposed)
                return;
            _sprites.Clear();
            _beams.Clear();

            float elapsed = MathHelper.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0f, TickSeconds * MaxTicksPerFrame);
            _tickAccumulator += elapsed;
            _ticksThisFrame = 0;
            while (_tickAccumulator >= TickSeconds && _ticksThisFrame < MaxTicksPerFrame)
            {
                _tickAccumulator -= TickSeconds;
                _ticksThisFrame++;
            }
            if (_ticksThisFrame == MaxTicksPerFrame)
                _tickAccumulator = MathF.Min(_tickAccumulator, TickSeconds);

            WorldTime = gameTime.TotalGameTime.TotalMilliseconds;
            foreach (var trail in _trails)
                trail.TouchedThisFrame = false;

            ModelObject owner = Owner;
            if (owner != null && !owner.Hidden && owner.Visible && owner.Model != null &&
                (owner.MaterialItemGroup >= 0 || CustomEmitter != null))
            {
                _bones = owner.GetBoneTransforms();
                if (_bones != null && _bones.Length > 0)
                {
                    _ownerWorld = owner.WorldPosition;
                    Character = FindCharacter(owner) ?? owner;
                    _characterRotation = ExtractRotation(Character.WorldPosition);
                    s_textureLoad ??= LoadTexturesAsync();
                    if (CustomEmitter != null)
                        CustomEmitter(this);
                    else
                        ItemLinkEffectEmitter.Emit(this, owner.MaterialItemGroup, owner.MaterialItemIndex, StandaloneItem);
                }
            }

            for (int t = 0; t < _ticksThisFrame; t++)
            {
                TickParticles();
                TickTrails();
                TickHelices();
                TickRibbons();
            }
        }

        /// <summary>
        /// CreateJoint(BITMAP_JOINT_ENERGY, p, p, o->Angle, 17 or 47, o, Scale): a ribbon that follows
        /// a point of the item (o->EyeLeft / EyeRight) and keeps its last 12 ticks.
        /// </summary>
        public void EnergyTrail(int slot, Vector3 position, Vector3 color, float width)
        {
            if ((uint)slot >= TrailSlots)
                return;
            var trail = _trails[slot];
            trail.TouchedThisFrame = true;
            trail.Latest = position;
            trail.Color = color;
            trail.Width = width;
            if (trail.Count == 0)
            {
                trail.Points[0] = position;
                trail.Count = 1;
            }
        }

        private void TickTrails()
        {
            foreach (var trail in _trails)
            {
                if (!trail.TouchedThisFrame)
                {
                    // The source point stopped emitting: let the ribbon drain.
                    if (trail.Count > 0)
                        trail.Count--;
                    continue;
                }

                int count = Math.Min(trail.Count + 1, TrailPoints);
                for (int i = count - 1; i > 0; i--)
                    trail.Points[i] = trail.Points[i - 1];
                trail.Points[0] = trail.Latest;
                trail.Count = count;
            }
        }

        private static ModelObject FindCharacter(ModelObject owner)
        {
            ModelObject current = owner;
            while (current.Parent is ModelObject parent)
                current = parent;
            return ReferenceEquals(current, owner) ? null : current;
        }

        private static Matrix ExtractRotation(Matrix world)
        {
            Vector3 right = world.Right, forward = world.Forward, up = world.Up;
            if (right.LengthSquared() < 1e-6f || up.LengthSquared() < 1e-6f)
                return Matrix.Identity;
            right.Normalize();
            forward.Normalize();
            up.Normalize();
            var m = Matrix.Identity;
            m.Right = right;
            m.Forward = forward;
            m.Up = up;
            return m;
        }

        // ───────────────────────────── Emitter helpers ─────────────────────────────

        /// <summary>b->TransformPosition(BoneTransform[bone], (x, y, z), Position, true).</summary>
        public Vector3 Bone(int bone, float x = 0f, float y = 0f, float z = 0f)
        {
            Vector3 local = (uint)bone < (uint)_bones.Length
                ? Vector3.Transform(new Vector3(x, y, z), _bones[bone])
                : new Vector3(x, y, z);
            return Vector3.Transform(local, _ownerWorld);
        }

        /// <summary>Bone position in the owner's model space (for effects attached as children).</summary>
        public Vector3 BoneLocal(int bone, float x = 0f, float y = 0f, float z = 0f) =>
            (uint)bone < (uint)_bones.Length
                ? Vector3.Transform(new Vector3(x, y, z), _bones[bone])
                : new Vector3(x, y, z);

        /// <summary>Converts a model-space position of the owner to world space.</summary>
        public Vector3 ToWorld(Vector3 local) => Vector3.Transform(local, _ownerWorld);

        /// <summary>CreateSprite(Type, Position, Scale, Light, Owner, Rotation).</summary>
        /// <summary>CreateSprite(..., SubType 1): subtractive sprite (darkens what is behind it).</summary>
        public void DarkSprite(Fx texture, Vector3 position, float scale, Vector3 color, float rotationDegrees = 0f)
        {
            if (_sprites.Count >= MaxSprites || scale <= 0f)
                return;
            _sprites.Add(new SpriteRequest
            {
                Texture = texture,
                Position = position,
                Scale = scale,
                Color = color,
                RotationDegrees = rotationDegrees,
                Subtract = true,
            });
        }

        /// <summary>
        /// One-frame joint between two points (BITMAP_JOINT_THUNDER / JOINT_SPIRIT created every tick
        /// between two bones): a camera-facing strip of the given width.
        /// </summary>
        public void BeamBetween(Fx texture, Vector3 from, Vector3 to, float width, Vector3 color)
        {
            if (_beams.Count < 32)
                _beams.Add(new Beam { Texture = texture, From = from, To = to, Width = width, Color = color });
        }

        /// <summary>CreateParticle(BITMAP_CHROME_ENERGY2, Position, Angle, Light, 0, Scale).</summary>
        public void EnergyParticle(Vector3 position, Vector3 color, float scale)
        {
            ref Particle p = ref Allocate();
            p.Live = true;
            p.Texture = Fx.Energy2;
            p.Kind = 200;
            p.Position = position;
            p.Velocity = Vector3.Zero;
            p.Color = color;
            p.Scale = scale * (_random.Next(64) + 128) * 0.01f;
            p.ScaleDecay = 0.04f;
            p.LifeTicks = 8;
            p.Rotation = _random.Next(360);
        }

        /// <summary>CreateParticle(BITMAP_SHINY, Position, Angle, Light, 5, Scale): small falling glints.</summary>
        public void ShinyParticle(Vector3 position, Vector3 color, float scale)
        {
            ref Particle p = ref Allocate();
            p.Live = true;
            p.Texture = Fx.Shiny0;
            p.Kind = 201;
            p.Position = position + new Vector3(_random.Next(10) - 5, _random.Next(10) - 5, 0f);
            p.Velocity = Vector3.Zero;
            p.Color = color;
            p.Scale = scale + _random.Next(10) * 0.02f;
            p.Gravity = -_random.Next(10) * 0.3f;
            p.LifeTicks = _random.Next(10) + 10;
            p.Rotation = _random.Next(360);
        }

        public void Sprite(Fx texture, Vector3 position, float scale, Vector3 color, float rotationDegrees = 0f)
        {
            if (_sprites.Count >= MaxSprites || scale <= 0f)
                return;
            _sprites.Add(new SpriteRequest
            {
                Texture = texture,
                Position = position,
                Scale = scale,
                Color = color,
                RotationDegrees = rotationDegrees,
            });
        }

        /// <summary>rand() % n.</summary>
        public int Rand(int n) => _random.Next(n);

        /// <summary>rand_fps_check(n): true with probability 1/n per 25 Hz tick.</summary>
        public bool Chance(int n)
        {
            if (_ticksThisFrame <= 0)
                return false;
            if (n <= 1)
                return true;
            double miss = Math.Pow(1.0 - 1.0 / n, _ticksThisFrame);
            return _random.NextDouble() >= miss;
        }

        public int CharacterAction => Character?.CurrentAction ?? -1;

        /// <summary>Velocity expressed in the character frame (SourceMain AngleMatrix(o->Angle)).</summary>
        private Vector3 CharacterFrame(Vector3 local) => Vector3.TransformNormal(local, _characterRotation);

        /// <summary>CreateParticle(BITMAP_SPARK + 1, Position, o->Angle, Light, 11 or 14, Scale).</summary>
        public void SparkParticle(Vector3 position, Vector3 color, int subType, float scale)
        {
            ref Particle p = ref Allocate();

            p.Live = true;
            p.Texture = Fx.Spark1;
            p.Kind = subType;
            p.Position = position;
            p.Color = color;
            p.Gravity = 0f;
            if (subType == 14)
            {
                if (_random.Next(2) == 0)
                {
                    p.Velocity = CharacterFrame(new Vector3(-4f + _random.Next(4), _random.Next(4), _random.Next(4)));
                    p.LifeTicks = 30;
                    p.Scale = _random.Next(5) / 10f + 0.4f;
                }
                else
                {
                    p.Velocity = Vector3.Zero;
                    p.LifeTicks = 2;
                    p.Scale = scale;
                }
                p.ScaleDecay = 0.01f;
            }
            else
            {
                p.Velocity = CharacterFrame(new Vector3(-4f + _random.Next(8), _random.Next(4), _random.Next(4)));
                p.LifeTicks = 30;
                p.Scale = _random.Next(5) / 10f + 1f;
                p.ScaleDecay = 0.04f;
            }
            p.ColorDecay = 1f / 1.05f;
        }

        /// <summary>CreateParticle(BITMAP_SPARK, Position, Angle, Light) with subtype 0 (falling sparks).</summary>
        public void GravitySpark(Vector3 position, float pitchDegrees)
        {
            ref Particle p = ref Allocate();

            float yaw = MathHelper.ToRadians(_random.Next(360));
            Matrix rotation = Matrix.CreateRotationX(0f) *
                              Matrix.CreateRotationY(MathHelper.ToRadians(pitchDegrees)) *
                              Matrix.CreateRotationZ(yaw);
            p.Live = true;
            p.Texture = Fx.Spark;
            p.Kind = 0;
            p.Position = position;
            p.Velocity = Vector3.TransformNormal(new Vector3(0f, (_random.Next(20) + 20) * 0.1f, 0f), rotation) * 2f;
            p.Scale = (_random.Next(4) + 4) * 0.1f;
            p.LifeTicks = _random.Next(16) + 24;
            p.Gravity = _random.Next(16) + 6;
            p.Color = Vector3.One;
        }

        /// <summary>
        /// CreateEffect(MODEL_STAR_SHINE, Position, Angle, Light, 0, ..., Scale): grows for ten ticks,
        /// shows a BITMAP_SHINY sprite for ten ticks and shrinks for the last ten.
        /// </summary>
        public void StarShine(Vector3 position, Vector3 color, float scale)
        {
            ref Particle p = ref Allocate();
            p.Live = true;
            p.Texture = Fx.Shiny0;
            p.Kind = 100;
            p.Position = position;
            p.Velocity = Vector3.Zero;
            p.Color = color;
            p.Scale = scale;
            p.LifeTicks = 30;
            p.Rotation = _random.Next(360);
        }

        private int _nextParticle;

        // False when no particle is live: idle emitters (most items) skip the particle array in ticks and draws.
        private bool _hasLiveParticles;

        private ref Particle Allocate()
        {
            _hasLiveParticles = true;
            for (int i = 0; i < _particles.Length; i++)
            {
                int index = (_nextParticle + i) % _particles.Length;
                if (!_particles[index].Live)
                {
                    _nextParticle = index + 1;
                    return ref _particles[index];
                }
            }
            // Pool exhausted: recycle the next slot like the original fixed particle array would refuse.
            _nextParticle = (_nextParticle + 1) % _particles.Length;
            return ref _particles[_nextParticle];
        }

        private void TickParticles()
        {
            if (!_hasLiveParticles)
                return;

            // Particles that die in this tick keep the flag set for one more tick; harmless.
            bool anyLive = false;
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;
                anyLive = true;

                p.LifeTicks -= 1f;
                if (p.LifeTicks <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Position += p.Velocity;
                if (p.Kind == 200)
                {
                    p.Scale -= 0.04f;
                    p.Rotation += 5f;
                    if (p.Scale <= 0f)
                        p.Live = false;
                    continue;
                }
                if (p.Kind == 201)
                {
                    p.Rotation += 10f;
                    p.Position.Z += p.Gravity;
                    p.Color /= 1.02f;
                    continue;
                }
                if (p.Kind == 100)
                {
                    float factor = p.LifeTicks > 20f ? 1.1f : p.LifeTicks <= 10f ? 0.9f : 1f;
                    p.Scale *= factor;
                    p.Color *= factor;
                }
                else if (p.Kind == 0)
                {
                    float luminosity = p.LifeTicks / 16f;
                    p.Color = new Vector3(luminosity);
                    p.Position.Z += p.Gravity;
                    p.Gravity -= 2f;
                }
                else
                {
                    p.Color *= p.ColorDecay;
                    p.Scale -= p.ScaleDecay;
                    if (p.Scale <= 0f)
                        p.Live = false;
                }
            }
            _hasLiveParticles = anyLive;
        }

        // ───────────────────────────── Rendering ─────────────────────────────

        private static async Task LoadTexturesAsync()
        {
            for (int i = 0; i < TexturePaths.Length; i++)
            {
                try
                {
                    s_textures[i] = await TextureLoader.Instance.PrepareAndGetTexture(TexturePaths[i]);
                }
                catch
                {
                    s_textures[i] = null;
                }
            }
        }

        /// <summary>Writes the effect's quads (into the world batch when it is open; run by <see cref="ItemEffectSystem"/>).</summary>
        internal override void RenderEffect()
        {
            if (IsDisposed || Owner is not { Visible: true })
                return;

            // Textures that have something to draw this frame. The passes below visit only these instead of
            // scanning every sprite and particle once per texture (27 textures).
            uint additiveTextures = 0, subtractTextures = 0;
            for (int i = 0; i < _sprites.Count; i++)
            {
                SpriteRequest s = _sprites[i];
                if (s.Subtract)
                    subtractTextures |= 1u << (int)s.Texture;
                else
                    additiveTextures |= 1u << (int)s.Texture;
            }
            for (int i = 0; _hasLiveParticles && i < _particles.Length; i++)
            {
                if (_particles[i].Live)
                    additiveTextures |= 1u << (int)_particles[i].Texture;
            }
            if (_trails[0].Count > 1 || _trails[1].Count > 1)
                additiveTextures |= 1u << (int)Fx.JointLaser;
            for (int i = 0; i < _beams.Count; i++)
                additiveTextures |= 1u << (int)_beams[i].Texture;
            if (_ribbons.Count > 0)
                additiveTextures |= 1u << (int)Fx.Flare;
            if (_helices.Count > 0)
                additiveTextures |= 1u << (int)Fx.JointEnergy;
            if (additiveTextures == 0 && subtractTextures == 0)
                return;
            if (s_textureLoad == null || !s_textureLoad.IsCompleted)
                return;

            ModelObject owner = Owner;
            float alpha = MathHelper.Clamp(owner?.TotalAlpha ?? 1f, 0f, 1f);
            if (alpha <= 0.01f)
                return;

            _vertices ??= new VertexPositionColorTexture[(MaxSprites + MaxParticles + 800) * 4];
            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;

            // Quads go into the world batch per texture and blend mode (drawn by EndWorldBatch).
            for (int t = 0; t < (int)Fx.Count; t++)
            {
                if ((additiveTextures & (1u << t)) == 0)
                    continue;
                Texture2D texture = s_textures[t];
                if (texture == null || texture.IsDisposed)
                    continue;

                int quads = 0;
                for (int i = 0; i < _sprites.Count; i++)
                {
                    SpriteRequest s = _sprites[i];
                    if ((int)s.Texture != t || s.Subtract)
                        continue;
                    WriteQuad(quads++, texture, s.Position, s.Scale, s.Color * alpha, s.RotationDegrees, right, up);
                }
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (!p.Live || (int)p.Texture != t)
                        continue;
                    // The star model itself is not reproduced; its sprite is visible mid-life.
                    if (p.Kind == 100 && (p.LifeTicks > 20f || p.LifeTicks <= 10f))
                        continue;
                    WriteQuad(quads++, texture, p.Position, p.Scale, p.Color * alpha, p.Rotation, right, up);
                }

                if (t == (int)Fx.JointLaser)
                {
                    foreach (var trail in _trails)
                        quads = WriteTrail(quads, trail, alpha);
                }
                for (int i = 0; i < _beams.Count; i++)
                {
                    if ((int)_beams[i].Texture == t)
                        quads = WriteBeam(quads, _beams[i], alpha);
                }

                if (t == (int)Fx.Flare)
                {
                    foreach (var ribbon in _ribbons)
                        quads = WriteRibbon(quads, ribbon, alpha);
                }

                if (t == (int)Fx.JointEnergy)
                {
                    Vector3 anchor = (Character ?? owner)?.WorldPosition.Translation ?? Vector3.Zero;
                    foreach (var helix in _helices)
                        quads = WriteHelix(quads, helix, anchor, alpha);
                }

                if (quads > 0)
                    AppendToBatch(t, subtract: false, quads);
            }

            // Subtractive sprites (EnableAlphaBlendMinus).
            for (int t = 0; t < (int)Fx.Count; t++)
            {
                if ((subtractTextures & (1u << t)) == 0)
                    continue;
                Texture2D texture = s_textures[t];
                if (texture == null || texture.IsDisposed)
                    continue;
                int quads = 0;
                for (int i = 0; i < _sprites.Count; i++)
                {
                    SpriteRequest s = _sprites[i];
                    if ((int)s.Texture == t && s.Subtract)
                        WriteQuad(quads++, texture, s.Position, s.Scale, s.Color * alpha, s.RotationDegrees, right, up);
                }
                if (quads > 0)
                    AppendToBatch(t, subtract: true, quads);
            }
        }

        // Batch: every ItemLinkEffect appends its quads per texture and blend mode, and they are drawn at once
        // afterwards (additive/subtractive, depth read: order independent) - after the world's DrawAfter passes,
        // or right away for an effect rendered outside a world pass (ItemEffectSystem.Draw).
        private static readonly VertexPositionColorTexture[][] s_batchVertices = new VertexPositionColorTexture[(int)Fx.Count * 2][];
        private static readonly int[] s_batchQuads = new int[(int)Fx.Count * 2];
        private static bool s_batchPending;
        private static BasicEffect s_batchEffect;
        private const int MaxQuadsPerBatchDraw = 8000;

        private void AppendToBatch(int texture, bool subtract, int quads)
        {
            int bucket = texture * 2 + (subtract ? 1 : 0);
            int start = s_batchQuads[bucket];
            int needed = (start + quads) * 4;
            var buffer = s_batchVertices[bucket];
            if (buffer == null || buffer.Length < needed)
            {
                Array.Resize(ref buffer, Math.Max(needed, Math.Max(256, (buffer?.Length ?? 0) * 2)));
                s_batchVertices[bucket] = buffer;
            }

            Array.Copy(_vertices, 0, buffer, start * 4, quads * 4);
            s_batchQuads[bucket] = start + quads;
            s_batchPending = true;
        }

        /// <summary>Draws and clears the collected quads; called by the world after its DrawAfter passes.</summary>
        internal static void EndWorldBatch(GraphicsDevice gd)
        {
            if (!s_batchPending || gd == null)
                return;

            s_batchPending = false;
            s_batchEffect ??= new BasicEffect(gd)
            {
                VertexColorEnabled = true,
                TextureEnabled = true,
                LightingEnabled = false,
                World = Matrix.Identity,
            };

            BlendState previousBlend = gd.BlendState;
            DepthStencilState previousDepth = gd.DepthStencilState;
            RasterizerState previousRasterizer = gd.RasterizerState;
            SamplerState previousSampler = gd.SamplerStates[0];
            try
            {
                gd.DepthStencilState = DepthStencilState.DepthRead;
                gd.RasterizerState = RasterizerState.CullNone;
                gd.SamplerStates[0] = SamplerState.LinearClamp;
                s_batchEffect.View = Camera.Instance.View;
                s_batchEffect.Projection = Camera.Instance.Projection;

                for (int pass = 0; pass < 2; pass++)
                {
                    gd.BlendState = pass == 0 ? Blendings.OneOneAdditive : s_minusBlend;
                    for (int t = 0; t < (int)Fx.Count; t++)
                    {
                        int bucket = t * 2 + pass;
                        int quads = s_batchQuads[bucket];
                        Texture2D texture = s_textures[t];
                        if (quads == 0 || texture == null || texture.IsDisposed)
                            continue;

                        s_batchEffect.Texture = texture;
                        s_batchEffect.CurrentTechnique.Passes[0].Apply();
                        for (int first = 0; first < quads; first += MaxQuadsPerBatchDraw)
                        {
                            int count = Math.Min(MaxQuadsPerBatchDraw, quads - first);
                            gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, s_batchVertices[bucket], first * 4,
                                count * 4, QuadIndexCache.Get(count), 0, count * 2);
                        }
                    }
                }
            }
            finally
            {
                Array.Clear(s_batchQuads);
                gd.BlendState = previousBlend;
                gd.DepthStencilState = previousDepth;
                gd.RasterizerState = previousRasterizer;
                gd.SamplerStates[0] = previousSampler;
            }
        }

        private int WriteBeam(int quad, Beam beam, float alpha)
        {
            // CreateTail with Angle (0,0,0): every tail is a cross of +-Scale/2 along world X and Z.
            var color = new Color(Vector3.Clamp(beam.Color * alpha, Vector3.Zero, Vector3.One));
            foreach (var side in new[] { Vector3.UnitX * (beam.Width * 0.5f), Vector3.UnitZ * (beam.Width * 0.5f) })
            {
                if (quad + 1 > _vertices.Length / 4)
                    return quad;
                int v = quad * 4;
                // RenderJoints: U runs along the joint, V across it.
                _vertices[v] = new VertexPositionColorTexture(beam.From - side, color, new Vector2(0f, 0f));
                _vertices[v + 1] = new VertexPositionColorTexture(beam.To - side, color, new Vector2(1f, 0f));
                _vertices[v + 2] = new VertexPositionColorTexture(beam.To + side, color, new Vector2(1f, 1f));
                _vertices[v + 3] = new VertexPositionColorTexture(beam.From + side, color, new Vector2(0f, 1f));
                quad++;
            }
            return quad;
        }

        private int WriteHelix(int quad, Helix helix, Vector3 anchor, float alpha)
        {
            if (helix.TailCount < 2)
                return quad;

            // Light = (1/11, 0.5/11, 1/11), Scale 10, cross-shaped joint tails.
            var color = new Color(new Vector3(1f / 11f, 0.5f / 11f, 1f / 11f) * alpha);
            Vector3 horizontal = Vector3.UnitX * 5f, vertical = Vector3.UnitZ * 5f;
            int capacity = _vertices.Length / 4;
            for (int i = 0; i < helix.TailCount - 1 && quad + 2 <= capacity; i++)
            {
                Vector3 a = anchor + helix.Tails[i], b = anchor + helix.Tails[i + 1];
                float vA = i / (float)(Helix.MaxTails - 1), vB = (i + 1) / (float)(Helix.MaxTails - 1);
                foreach (var offset in new[] { horizontal, vertical })
                {
                    int v = quad * 4;
                    _vertices[v] = new VertexPositionColorTexture(a - offset, color, new Vector2(vA, 0f));
                    _vertices[v + 1] = new VertexPositionColorTexture(b - offset, color, new Vector2(vB, 0f));
                    _vertices[v + 2] = new VertexPositionColorTexture(b + offset, color, new Vector2(vB, 1f));
                    _vertices[v + 3] = new VertexPositionColorTexture(a + offset, color, new Vector2(vA, 1f));
                    quad++;
                }
            }
            return quad;
        }

        private int WriteTrail(int quad, Trail trail, float alpha)
        {
            if (trail.Count < 2)
                return quad;

            // Joint tails: one strip across the character's X axis and one along world Z.
            Vector3 across = Vector3.TransformNormal(Vector3.UnitX, _characterRotation) * (trail.Width * 0.5f);
            Vector3 vertical = Vector3.UnitZ * (trail.Width * 0.5f);
            int capacity = _vertices.Length / 4;
            for (int i = 0; i < trail.Count - 1 && quad + 2 <= capacity; i++)
            {
                Vector3 a = i == 0 ? trail.Latest : trail.Points[i];
                Vector3 b = trail.Points[i + 1];
                float fadeA = 1f - i / (float)TrailPoints;
                float fadeB = 1f - (i + 1) / (float)TrailPoints;
                var colorA = new Color(Vector3.Clamp(trail.Color * fadeA * alpha, Vector3.Zero, Vector3.One));
                var colorB = new Color(Vector3.Clamp(trail.Color * fadeB * alpha, Vector3.Zero, Vector3.One));
                float vA = i / (float)(TrailPoints - 1), vB = (i + 1) / (float)(TrailPoints - 1);
                foreach (var offset in new[] { across, vertical })
                {
                    int v = quad * 4;
                    _vertices[v] = new VertexPositionColorTexture(a - offset, colorA, new Vector2(vA, 0f));
                    _vertices[v + 1] = new VertexPositionColorTexture(b - offset, colorB, new Vector2(vB, 0f));
                    _vertices[v + 2] = new VertexPositionColorTexture(b + offset, colorB, new Vector2(vB, 1f));
                    _vertices[v + 3] = new VertexPositionColorTexture(a + offset, colorA, new Vector2(vA, 1f));
                    quad++;
                }
            }
            return quad;
        }

        private void WriteQuad(int quad, Texture2D texture, Vector3 center, float scale, Vector3 color,
            float rotationDegrees, Vector3 cameraRight, Vector3 cameraUp)
        {
            // RenderSprite: Width = Bitmap->Width * Scale, Height = Bitmap->Height * Scale.
            float halfWidth = texture.Width * scale * 0.5f;
            float halfHeight = texture.Height * scale * 0.5f;
            Vector3 right = cameraRight, up = cameraUp;
            if (rotationDegrees != 0f)
            {
                float radians = MathHelper.ToRadians(rotationDegrees);
                float c = MathF.Cos(radians), s = MathF.Sin(radians);
                right = cameraRight * c + cameraUp * s;
                up = cameraUp * c - cameraRight * s;
            }
            right *= halfWidth;
            up *= halfHeight;

            var tint = new Color(Vector3.Clamp(color, Vector3.Zero, Vector3.One));
            int v = quad * 4;
            _vertices[v] = new VertexPositionColorTexture(center - right - up, tint, new Vector2(0f, 1f));
            _vertices[v + 1] = new VertexPositionColorTexture(center + right - up, tint, new Vector2(1f, 1f));
            _vertices[v + 2] = new VertexPositionColorTexture(center + right + up, tint, new Vector2(1f, 0f));
            _vertices[v + 3] = new VertexPositionColorTexture(center - right + up, tint, new Vector2(0f, 0f));
        }

    }
}
