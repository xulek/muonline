#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Worlds.Elbeland
{
    /// <summary>
    /// GOBoid.cpp MoveTornado (WD_51HOME_6TH_CHAR, boid slot 0), verbatim:
    /// Scale forced to 1.0; heading re-roll rand_fps_check(500) -> (rand()%314)/100 rad;
    /// drift X += sin(h)*2, Y += cos(h)*2 per reference frame (=50 u/s);
    /// Angle.Z stays 0; BlendMeshLight ramps +0.1/frame up to 1. Action PlaySpeed is
    /// 0.1 at the BMD level with the standard x25 key rate -> client AnimationSpeed 25.
    /// </summary>
    public sealed class ElbelandTornadoSystem : EffectObject
    {
        private const float ReferenceFps = 25f;
        private const string TornadoModelPath = "Object52/typhoonall.bmd";
        private const float DespawnRange = 1500f;

        private readonly WalkableWorldControl _world;
        private ElbelandTornado? _tornado;
        private float _spawnCooldown = 3f;

        public ElbelandTornadoSystem(WalkableWorldControl world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            var walker = _world.Walker;
            if (_world.Status != GameControlStatus.Ready || walker == null)
                return;

            Vector3 heroPosition = walker.Position;

            if (_tornado != null)
            {
                float distSq = Vector2.DistanceSquared(
                    new Vector2(_tornado.Position.X, _tornado.Position.Y),
                    new Vector2(heroPosition.X, heroPosition.Y));

                // Default boid FlyDistance (1500) applies — no special case for tornados
                if (_tornado.Status == GameControlStatus.Disposed || distSq >= DespawnRange * DespawnRange)
                {
                    Children.Remove(_tornado);
                    _tornado.Dispose();
                    _tornado = null;
                }
            }

            _spawnCooldown -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_spawnCooldown > 0f || _tornado != null)
                return;

            // rand_fps_check(500): expected one spawn attempt per ~20 s
            _spawnCooldown = 20f;

            double tornadoSpawnAngle = MuGame.Random.NextDouble() * Math.PI * 2.0;
            float tornadoSpawnDist = 650f + (float)MuGame.Random.NextDouble() * 450f;
            Vector3 spawnPos = new(
                heroPosition.X + MathF.Cos((float)tornadoSpawnAngle) * tornadoSpawnDist,
                heroPosition.Y + MathF.Sin((float)tornadoSpawnAngle) * tornadoSpawnDist,
                heroPosition.Z);

            const int terrainSize = Constants.TERRAIN_SIZE;
            int tileX = (int)(spawnPos.X / Constants.TERRAIN_SCALE);
            int tileY = (int)(spawnPos.Y / Constants.TERRAIN_SCALE);
            if (tileX < 0 || tileX >= terrainSize || tileY < 0 || tileY >= terrainSize)
                return;

            var flag = _world.Terrain.RequestTerrainFlag(tileX, tileY);
            if (flag.HasFlag(Client.Data.ATT.TWFlags.NoGround) || flag.HasFlag(Client.Data.ATT.TWFlags.SafeZone))
                return;

            spawnPos.Z = _world.Terrain.RequestTerrainHeight(spawnPos.X, spawnPos.Y);

            _tornado = new ElbelandTornado
            {
                Position = spawnPos,
                HeadingAngle = MuGame.Random.Next(314) / 100f
            };
            Children.Add(_tornado);
            _ = _tornado.Load();
        }

        public override void Dispose()
        {
            _tornado?.Dispose();
            _tornado = null;
            base.Dispose();
        }
    }

    public sealed class ElbelandTornado : ModelObject
    {
        private const float ReferenceFps = 25f;

        private float _lightRamp;

        public float HeadingAngle { get; set; }

        public override async Task Load()
        {
            if (Status != GameControlStatus.NonInitialized)
                return;

            Model = await BMDLoader.Instance.Prepare("Object52/typhoonall.bmd");
            if (Model != null)
            {
                // Actions[0].PlaySpeed = 0.1 (MapManager.cpp:1186) x global PlaySpeed 1.0
                // -> 0.1 * 25 = 2.5 keys/s -> client AnimationSpeed 25.
                CurrentAction = 0;
                AnimationSpeed = 25f;
            }
            await base.Load();

            Scale = 1.0f; // MoveTornado forces Scale = 1.0
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            float legacyFrames = dt * ReferenceFps;

            // rand_fps_check(500): per-frame 1/500 chance of a new heading
            if ((float)MuGame.Random.NextDouble() < legacyFrames / 500f)
                HeadingAngle = MuGame.Random.Next(314) / 100f;

            Position += new Vector3(MathF.Sin(HeadingAngle), MathF.Cos(HeadingAngle), 0f)
                        * 2f * ReferenceFps * dt;

            // Source has no clamp (flat town plaza); guard against burying into hills
            // when the wander carries the tornado onto raised terrain.
            if (World?.Terrain != null)
            {
                float groundZ = World.Terrain.RequestTerrainHeight(Position.X, Position.Y);
                if (Position.Z < groundZ)
                    Position = new Vector3(Position.X, Position.Y, groundZ);
            }
            if (_lightRamp < 1f)
            {
                _lightRamp = MathF.Min(1f, _lightRamp + 0.1f * legacyFrames);
                BlendMeshLight = _lightRamp;
            }
        }
    }

    /// <summary>
    /// GOBoid.cpp MoveEagle fed by GMNewTown Type-62 objects, verbatim:
    /// Velocity=0, Scale=0.5, AlphaEnable fade-in, Gravity=10*objectScale, seed phase
    /// (rand%314)/100; circular wander HeadAngle = cos/sin(WorldTime*0.001 + seed) *
    /// Gravity * factor with a three-step FLY/GROUND flip state (figure-eight path);
    /// Z bob += sin(WorldTime*0.0005); bank Angle[1] += sin(WorldTime*0.001)*0.4;
    /// Angle.Z = fAngle + 270. Action PlaySpeed 0.5 -> client AnimationSpeed 12.5.
    /// </summary>
    public sealed class ElbelandEagle : ModelObject
    {
        private const float ReferenceFps = 25f;
        private const float EagleModelSpeed = 0.5f;

        private readonly float _seedOffset;
        private readonly float _gravityStep;
        private int _pathState;          // HeadAngle[2]: 0/1/2 flip detector
        private bool _groundArc;         // o->AI == BOID_GROUND

        public bool IsRecyclable { get; private set; }

        public ElbelandEagle()
        {
            RenderShadow = false;
            Scale = 0.5f;
            Alpha = 0f;
            LightEnabled = true;
            Light = new Vector3(0.5f);
            _seedOffset = MuGame.Random.Next(314) / 100f;
            _gravityStep = 10f; // Gravity = 10 * pObject->Scale (scene objects are scale 1)
        }

        public override async Task Load()
        {
            if (Status != GameControlStatus.NonInitialized)
                return;

            Model = await BMDLoader.Instance.Prepare("Object52/sos3bi01.bmd");
            if (Model != null)
            {
                CurrentAction = 0;
                AnimationSpeed = EagleModelSpeed * ReferenceFps; // 12.5 keys/s
            }
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            double worldSec = gameTime.TotalGameTime.TotalMilliseconds * 0.001;
            float factor = MathF.Min(dt * ReferenceFps, 1f);

            float seedAngle = (float)(worldSec * 1.0) + _seedOffset;
            float flyRange = _gravityStep * factor;

            float hx, hy;
            if (!_groundArc)
            {
                hx = MathF.Cos(seedAngle) * flyRange;
                hy = MathF.Sin(seedAngle) * flyRange;

                if (_pathState == 0 && hx > hy) _pathState = 1;
                else if (_pathState == 1 && hx < hy)
                {
                    _pathState = 2;
                }
                else if (_pathState == 2 && hx > hy)
                {
                    _groundArc = true;
                    _pathState = 0;
                    hx = MathF.Sin(seedAngle) * flyRange;
                    hy = MathF.Cos(seedAngle) * flyRange;
                }
            }
            else
            {
                hx = MathF.Sin(seedAngle) * flyRange;
                hy = MathF.Cos(seedAngle) * flyRange;

                if (_pathState == 0 && hx < hy) _pathState = 1;
                else if (_pathState == 1 && hx > hy)
                {
                    _pathState = 2;
                }
                else if (_pathState == 2 && hx < hy)
                {
                    _groundArc = false;
                    _pathState = 0;
                    hx = MathF.Cos(seedAngle) * flyRange * factor;
                    hy = MathF.Sin(seedAngle) * flyRange * factor;
                }
            }

            Position += new Vector3(hx, hy, 0f);

            // MoveEagle: cumulative bank on Angle[1] += sin(WorldTime*0.001)*0.4 per frame
            // (bounded oscillation; source never resets it either), and vertical bob
            // Position[2] += sin(WorldTime*0.0005)*1.0 — replicated as the closed-form
            // integral of that sine so the drift stays bounded like the original sum.
            _bankAngle += MathF.Sin((float)worldSec * 0.001f) * 0.4f * factor;

            const float bobOmegaPerFrame = 0.0005f / ReferenceFps;
            float bobPhase = (float)worldSec * 0.0005f + _bobSeed;
            float bobAmplitude = factor / (2f * MathF.Sin(bobOmegaPerFrame * 0.5f));
            float bobOffset = bobAmplitude * (-MathF.Cos(bobPhase));

            float travelDeg = MathHelper.ToDegrees(MathF.Atan2(hx, -hy));
            Angle = new Vector3(
                Angle.X,
                _bankAngle,
                MathHelper.ToRadians(travelDeg));

            if (_baseZ < 0f)
                _baseZ = Position.Z;

            // Fade-in (Alpha 0 -> AlphaTarget 1)
            if (Alpha < 1f)
                Alpha = MathF.Min(1f, Alpha + dt * 1.25f);
        }

        private float _bankAngle;
        private float _bobSeed = (float)(MuGame.Random.NextDouble() * Math.PI * 2.0);
        private float _baseZ = -1f;
    }

    /// <summary>
    /// Ambient stand-in for the Type-62 scene-object anchors: keeps up to three eagles
    /// circling near the hero, recycling slots at the default boid FlyDistance (1500).
    /// </summary>
    public sealed class ElbelandEagleSystem : EffectObject
    {
        private const float ReferenceFps = 25f;
        private const float DespawnRange = 1500f;

        private readonly WalkableWorldControl _world;
        private readonly int _maxEagles;
        private readonly List<ElbelandEagle> _eagles = new();
        private float _spawnCooldown = 2f;

        public ElbelandEagleSystem(WalkableWorldControl world, int maxEagles)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _maxEagles = Math.Max(1, maxEagles);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            var walker = _world.Walker;
            if (_world.Status != GameControlStatus.Ready || walker == null)
                return;

            Vector3 heroPosition = walker.Position;

            for (int i = _eagles.Count - 1; i >= 0; i--)
            {
                var eagle = _eagles[i];
                float distSq = Vector2.DistanceSquared(
                    new Vector2(eagle.Position.X, eagle.Position.Y),
                    new Vector2(heroPosition.X, heroPosition.Y));

                if (eagle.Status == GameControlStatus.Disposed || distSq > DespawnRange * DespawnRange)
                {
                    Children.Remove(eagle);
                    eagle.Dispose();
                    _eagles.RemoveAt(i);
                }
            }

            _spawnCooldown -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_spawnCooldown > 0f || _eagles.Count >= _maxEagles)
                return;

            _spawnCooldown = 4f;


            double spawnAngle = MuGame.Random.NextDouble() * Math.PI * 2.0;
            float spawnDist = 650f + (float)MuGame.Random.NextDouble() * 450f;
            Vector3 spawnPos = new(
                heroPosition.X + MathF.Cos((float)spawnAngle) * spawnDist,
                heroPosition.Y + MathF.Sin((float)spawnAngle) * spawnDist,
                heroPosition.Z + 200f + MuGame.Random.Next(200));

            var newEagle = new ElbelandEagle
            {
                Position = new Vector3(
                    spawnPos.X,
                    spawnPos.Y,
                    spawnPos.Z)
            };

            _eagles.Add(newEagle);
            Children.Add(newEagle);
            _ = newEagle.Load();
        }

        public override void Dispose()
        {
            foreach (var eagle in _eagles)
                eagle.Dispose();
            _eagles.Clear();
            base.Dispose();
        }
    }
}

