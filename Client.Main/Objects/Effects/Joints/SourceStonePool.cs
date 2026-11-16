#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controls;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// MODEL_STONE1 / MODEL_STONE2 debris (ZzzEffect.cpp:2760 init default, 11007-11075 move) with the
    /// BITMAP_FIRE sub 1..3 children of sub 0 (ZzzEffectParticle.cpp:398, 4721, 9112).
    /// sub 0: pre-rotated Direction, falls and bounces; sub 1: zero Direction, rises; sub 2: rises and spins;
    /// sub 10: like sub 0 with a faster throw (x0.2, LifeTime rand%16+32, Scale (rand%4+15)*0.05, Gravity rand%16+28), no fire.
    /// sub 14 (CreateBomb3 with fScale != 1): ZzzEffect.cpp:2797 (LifeTime rand%16+20, Scale (rand%13+3)*0.08 * Scale,
    /// Gravity rand%3+3, HeadAngle = rot(0, (rand%128+64)*0.1, 0) + (0,0,15F)), 10968 (ballistic HeadAngle, spin
    /// 0.5 * LifeTime, ground: xy *= 0.6^F, z += LifeTime * F, Alpha -= 0.1F), BodyLight = terrain + Light.
    /// </summary>
    internal sealed class SourceStonePool
    {
        private static readonly string[] StonePaths = { "Skill/Stone01.bmd", "Skill/Stone02.bmd" };
        private const string FireTexturePath = "Effect/Fire01.jpg";
        private const float ModelVelocity = 0.3f;

        private struct Stone
        {
            public bool Live;
            public int Model;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Direction;
            public Vector3 Light;
            public Vector3 HeadAngle;
            public float Alpha;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private struct Fire
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public Vector3 Light;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Stone[] _stones;
        private readonly Fire[] _fires;
        private readonly SourceModelRenderer?[] _renderers = new SourceModelRenderer?[2];
        private Texture2D? _fireTexture;
        private float _frame;

        public SourceStonePool(int maxStones, int maxFires)
        {
            _stones = new Stone[maxStones];
            _fires = new Fire[maxFires];
        }

        public bool HasLive
        {
            get
            {
                for (int i = 0; i < _stones.Length; i++) if (_stones[i].Live) return true;
                for (int i = 0; i < _fires.Length; i++) if (_fires[i].Live) return true;
                return false;
            }
        }

        public async Task LoadAsync(WorldControl? world)
        {
            await Client.Main.Content.TextureLoader.Instance.Prepare(FireTexturePath);
            _fireTexture = await Client.Main.Content.TextureLoader.Instance.PrepareAndGetTexture(FireTexturePath);
            for (int i = 0; i < 2; i++)
            {
                var renderer = new SourceModelRenderer(StonePaths[i], -1);
                if (!await renderer.LoadInto(world))
                    continue;
                renderer.AnimationSpeed = 0f;
                renderer.UseNormalBlend();
                _renderers[i] = renderer;
            }
        }

        /// <summary>CreateEffect(MODEL_STONE1 + rand() % 2, position, angle, light, subType).</summary>
        public void Spawn(Vector3 position, Vector3 angleDeg, Vector3 light, int subType)
        {
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (s.Live)
                    continue;

                if (!AnyStone())
                    _frame = 0f;

                s.Live = true;
                s.Model = MuGame.Random.Next(2);
                s.SubType = subType;
                s.Position = position;
                s.Light = light;
                s.Angle = angleDeg;
                s.Alpha = 1f;
                s.HeadAngle = Vector3.Zero;
                s.LifeTime = MuGame.Random.Next(16) + 32;
                s.Angle.Z = MuGame.Random.Next(360);
                if (subType == 10)
                {
                    s.Scale = (MuGame.Random.Next(4) + 15) * 0.05f;
                    s.Direction = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(256) + 64) * 0.2f, 0f), s.Angle);
                    s.Gravity = MuGame.Random.Next(16) + 28;
                }
                else
                {
                    s.Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
                    Vector3 local = subType == 1 ? Vector3.Zero : new Vector3(0f, (MuGame.Random.Next(256) + 64) * 0.1f, 0f);
                    s.Direction = SourceJointMath.Rotate(local, s.Angle);
                    s.Gravity = MuGame.Random.Next(16) + 8;
                }
                return;
            }
        }

        /// <summary>CreateEffect(MODEL_STONE1 + rand() % 2, position, angle, light, 14, NULL, ..., scale).</summary>
        public void SpawnSub14(Vector3 position, Vector3 angleDeg, Vector3 light, float scale, float factor)
        {
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (s.Live)
                    continue;

                if (!AnyStone())
                    _frame = 0f;

                s.Live = true;
                s.Model = MuGame.Random.Next(2);
                s.SubType = 14;
                s.Position = position;
                s.Light = light;
                s.Angle = angleDeg;
                s.Alpha = 1f;
                s.Direction = Vector3.Zero;
                s.LifeTime = MuGame.Random.Next(16) + 20;
                s.Scale = (MuGame.Random.Next(13) + 3) * 0.08f * scale;
                s.Gravity = MuGame.Random.Next(3) + 3;
                s.Angle.Z = MuGame.Random.Next(360);
                s.HeadAngle = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(128) + 64) * 0.1f, 0f), s.Angle);
                s.HeadAngle.Z += 15f * factor;
                return;
            }
        }

        private bool AnyStone()
        {
            for (int i = 0; i < _stones.Length; i++) if (_stones[i].Live) return true;
            return false;
        }

        public void Move(float factor, TerrainControl? terrain)
        {
            bool any = false;
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (!s.Live)
                    continue;

                any = true;
                if (s.SubType == 14)
                {
                    s.HeadAngle.Z -= s.Gravity * factor;
                    s.Position += s.HeadAngle * factor;
                    float ground = terrain?.RequestTerrainHeight(s.Position.X, s.Position.Y) ?? s.Position.Z;
                    s.Angle.X += 0.5f * s.LifeTime * factor;
                    s.Angle.Y += 0.5f * s.LifeTime * factor;
                    if (s.Position.Z <= ground)
                    {
                        float drag = MathF.Pow(0.6f, factor);
                        s.Position.Z = ground;
                        s.HeadAngle.X *= drag;
                        s.HeadAngle.Y *= drag;
                        s.HeadAngle.Z += s.LifeTime * factor;
                        if (s.HeadAngle.Z < 0.5f)
                            s.HeadAngle.Z = 0f;
                        s.Alpha -= 0.1f * factor;
                    }

                    s.LifeTime -= factor;
                    if (s.LifeTime <= 0f)
                        s.Live = false;
                    continue;
                }

                if (s.SubType == 0 || s.SubType == 10)
                    s.Position += s.Direction * factor;
                else
                    s.Position += SourceJointMath.Rotate(s.Direction, s.Angle) * factor;
                s.Direction *= 0.9f; // per frame
                s.Position.Z += s.Gravity * factor;

                if (s.SubType == 0 || s.SubType == 10)
                {
                    s.Gravity -= 3f * factor;
                    float height = terrain?.RequestTerrainHeight(s.Position.X, s.Position.Y) ?? s.Position.Z;
                    if (s.Position.Z < height)
                    {
                        s.Position.Z = height;
                        s.Gravity = -s.Gravity * 0.5f;
                        s.LifeTime -= 4f * factor;
                        s.Angle.X -= s.Scale * 128f * factor;
                    }
                    else
                    {
                        s.Angle.X -= s.Scale * 32f * factor;
                    }

                    if (s.SubType == 0 && FpsCheck(10, factor) && FpsCheck(1, factor))
                        SpawnFire(s.Position, s.Angle, s.Light, 1 + MuGame.Random.Next(3));
                }
                else if (s.SubType == 1)
                {
                    s.Gravity += 0.5f * factor;
                }
                else
                {
                    s.Angle.Z += 20f * factor;
                    s.Gravity += 0.5f * factor;
                }

                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
            if (any)
                _frame += ModelVelocity * factor;

            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire p = ref _fires[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                p.Gravity += 0.004f * factor;
                p.Scale += p.Gravity * factor;
                p.Velocity *= 0.98f; // per frame
                p.Position.Z += p.Gravity * 10f * factor;
            }
        }

        private void SpawnFire(Vector3 position, Vector3 angle, Vector3 light, int subType)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire p = ref _fires[i];
                if (p.Live)
                    continue;

                p = default;
                p.Live = true;
                p.SubType = subType;
                p.Position = position;
                p.Angle = angle;
                p.Light = light;
                p.LifeTime = 24f;
                if (subType == 1)
                {
                    p.Velocity = new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f);
                    p.Scale = (MuGame.Random.Next(4) + 10) * 0.01f;
                }
                else
                {
                    p.Velocity = new Vector3(0f, -(MuGame.Random.Next(32) - 16) * 0.1f, 0f);
                    p.Scale = 1f;
                }
                return;
            }
        }

        public void DrawModels(GameTime gameTime, TerrainControl? terrain)
        {
            for (int m = 0; m < 2; m++)
            {
                SourceModelRenderer? renderer = _renderers[m];
                if (renderer == null)
                    continue;

                bool updated = false;
                for (int i = 0; i < _stones.Length; i++)
                {
                    ref Stone s = ref _stones[i];
                    if (!s.Live || s.Model != m)
                        continue;

                    if (!updated)
                    {
                        renderer.SetAnimationTime(_frame);
                        renderer.Update(gameTime);
                        updated = true;
                    }

                    Vector3 terrainLight = terrain?.EvaluateTerrainLight(s.Position.X, s.Position.Y) ?? Vector3.One;
                    Vector3 light = s.SubType == 14 ? terrainLight + s.Light : terrainLight * s.Light;
                    renderer.DrawInstance(s.Position,
                        new Vector3(MathHelper.ToRadians(s.Angle.X), MathHelper.ToRadians(s.Angle.Y), MathHelper.ToRadians(s.Angle.Z)),
                        s.Scale, light, 1f, s.Alpha);
                }
            }
        }

        public void DrawFires(SourceQuadBatch batch)
        {
            if (_fires.Length == 0 || _fireTexture == null || !batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire p = ref _fires[i];
                if (!p.Live)
                    continue;

                int frame = (int)((23f - p.LifeTime) / 6f);
                batch.AddSpriteRegion(p.Position, _fireTexture.Width * p.Scale * 0.25f, _fireTexture.Height * p.Scale, p.Light, p.Angle.X,
                    frame % 4 * 0.25f, 0f, 0.25f, 1f);
            }
            batch.End();
        }

        public void Dispose()
        {
            _renderers[0]?.Dispose();
            _renderers[1]?.Dispose();
        }

        private static bool FpsCheck(int referenceFrames, float factor)
            => MuGame.Random.NextDouble() < MathHelper.Clamp(factor / referenceFrames, 0f, 1f);
    }
}
