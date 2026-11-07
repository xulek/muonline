#nullable enable
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Original-style Dark Lord Fire Burst: three independently homing PierPart streams,
    /// a short chain of stationary fire segments and two expanding DarkLordSkill cast flashes.
    /// The legacy movement and emission logic runs at 25 Hz while rendering interpolates the
    /// three stream heads between ticks.
    /// </summary>
    public sealed class FireBurstEffect : EffectObject
    {
        private const string DefaultPierPartModelPath = "Skill/pierpart.bmd";
        private const string DefaultCastFlashModelPath = "Skill/darklordskill.bmd";
        private const string ImpactSoundPath = "Sound/eFirebustBoom.wav";

        private const float LegacyStepSeconds = 1f / 25f;
        private const int MaxLegacyStepsPerFrame = 5;
        private const int StreamCount = 3;
        private const int MaxTrailSegments = 96;
        private const int MaxBillboardQuads = MaxTrailSegments;
        private const float DirectionStep = 26f;

        private static readonly float[] InitialYawOffsets = { 90f, 0f, -90f };
        private static readonly float[] TargetHeightRatios = { 1f, 0.5f, 0.5f };
        private static readonly Vector3 FireColor = new(1f, 0.38f, 0.08f);
        private static readonly Vector3 TrailModelColor = new(1f, 0.52f, 0.18f);
        private static readonly Vector3 CastFlashColor = new(1f, 0.6f, 0.3f);

        private readonly WalkerObject _caster;
        private readonly ushort _targetId;
        private readonly WalkableWorldControl _walkableWorld;
        private readonly Vector3? _fallbackTargetPosition;
        private readonly StreamState[] _streams = new StreamState[StreamCount];
        private readonly TrailSegment[] _trailSegments = new TrailSegment[MaxTrailSegments];
        private readonly CastFlashState[] _castFlashes = new CastFlashState[2];
        private readonly VertexPositionColorTexture[] _billboardVertices =
            new VertexPositionColorTexture[MaxBillboardQuads * 4];
        private static readonly short[] BillboardIndices = QuadIndexCache.Get(MaxBillboardQuads);

        private SharedModelRenderer? _streamRenderer;
        private SharedModelRenderer? _trailRenderer;
        private SharedModelRenderer? _castFlashRenderer;
        private Texture2D? _fireTexture;
        private BasicEffect? _billboardEffect;
        private string _pierPartModelPath = DefaultPierPartModelPath;
        private string _castFlashModelPath = DefaultCastFlashModelPath;
        private Vector3 _startPosition;
        private float _legacyAccumulator;
        private float _renderInterpolation;
        private int _trailWriteCursor;
        private bool _initialized;
        private bool _impactSoundPlayed;
        private bool _disposed;

        private struct StreamState
        {
            public bool Active;
            public Vector3 PreviousPosition;
            public Vector3 Position;
            public float YawDegrees;
            public float PitchDegrees;
            public float SubstepCounter;
            public float TurnVelocityDegrees;
            public float TargetHeightRatio;
            public int LifeTicks;
        }

        private struct TrailSegment
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 AngleRadians;
            public float Intensity;
            public float Rotation;
            public int LifeTicks;
            public int MaxLifeTicks;
        }

        private struct CastFlashState
        {
            public bool Active;
            public Vector3 AngleRadians;
            public float PreviousScale;
            public float Scale;
            public float ExpansionVelocity;
            public float Brightness;
            public int LifeTicks;
        }

        public FireBurstEffect(
            WalkerObject caster,
            ushort targetId,
            WalkableWorldControl world,
            Vector3? fallbackTargetPosition)
        {
            _caster = caster ?? throw new ArgumentNullException(nameof(caster));
            _targetId = targetId;
            _walkableWorld = world ?? throw new ArgumentNullException(nameof(world));
            _fallbackTargetPosition = fallbackTargetPosition;

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-1800f, -1800f, -500f),
                new Vector3(1800f, 1800f, 900f));

        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _pierPartModelPath = await ResolveModelPath(
                DefaultPierPartModelPath,
                "Skill/PierPart.bmd",
                "Skill/pierPart.bmd");
            _castFlashModelPath = await ResolveModelPath(
                DefaultCastFlashModelPath,
                "Skill/DarkLordSkill.bmd",
                "Skill/darkLordSkill.bmd");

            _streamRenderer = new SharedModelRenderer(
                _pierPartModelPath,
                hiddenMesh: 1,
                blendMesh: -2);
            _trailRenderer = new SharedModelRenderer(
                _pierPartModelPath,
                hiddenMesh: 0,
                blendMesh: -2);
            _castFlashRenderer = new SharedModelRenderer(
                _castFlashModelPath,
                hiddenMesh: -1,
                blendMesh: -2);

            if (!await LoadRenderer(_streamRenderer) ||
                !await LoadRenderer(_trailRenderer) ||
                !await LoadRenderer(_castFlashRenderer))
            {
                return;
            }

            _fireTexture = await PrepareFirstTexture(
                "Effect/fire02.jpg",
                "Effect/Fire02.jpg",
                "Effect/fire02.OZJ",
                "Effect/Fire02.OZJ");

            if (_disposed || World == null)
                return;

            _billboardEffect = new BasicEffect(GraphicsDevice)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
                FogEnabled = false,
                World = Matrix.Identity
            };
        }

        private async Task<bool> LoadRenderer(SharedModelRenderer? renderer)
        {
            if (_disposed || renderer == null || World == null)
                return false;

            WorldControl world = World;
            renderer.World = world;
            await renderer.Load();
            return !_disposed && ReferenceEquals(World, world);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            if (_caster.Status == GameControlStatus.Disposed || _caster.World != _walkableWorld)
            {
                RemoveSelf();
                return;
            }

            if (!_initialized)
                InitializeEffect();

            _streamRenderer?.Update(gameTime);
            _trailRenderer?.Update(gameTime);
            _castFlashRenderer?.Update(gameTime);

            float elapsedSeconds = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                LegacyStepSeconds * MaxLegacyStepsPerFrame);
            _legacyAccumulator += elapsedSeconds;

            int steps = 0;
            while (_legacyAccumulator >= LegacyStepSeconds && steps < MaxLegacyStepsPerFrame)
            {
                TickLegacy();
                _legacyAccumulator -= LegacyStepSeconds;
                steps++;
            }

            if (steps == MaxLegacyStepsPerFrame)
                _legacyAccumulator = MathF.Min(_legacyAccumulator, LegacyStepSeconds);

            _renderInterpolation = MathHelper.Clamp(
                _legacyAccumulator / LegacyStepSeconds,
                0f,
                1f);

            if (!HasActiveContent())
                RemoveSelf();
        }

        private void InitializeEffect()
        {
            _initialized = true;
            _startPosition = ResolveStartPosition();
            Position = _startPosition;
            float casterYawDegrees = MathHelper.ToDegrees(_caster.Angle.Z);

            for (int i = 0; i < _streams.Length; i++)
            {
                _streams[i] = new StreamState
                {
                    Active = true,
                    PreviousPosition = _startPosition,
                    Position = _startPosition,
                    YawDegrees = NormalizeDegrees(casterYawDegrees + InitialYawOffsets[i]),
                    PitchDegrees = 0f,
                    SubstepCounter = 2f,
                    TurnVelocityDegrees = 10f,
                    TargetHeightRatio = TargetHeightRatios[i],
                    LifeTicks = 20,
                };
            }

            float casterYawRadians = MathHelper.ToRadians(casterYawDegrees);
            _castFlashes[0] = CreateCastFlash(
                new Vector3(
                    MathHelper.ToRadians(45f),
                    MathHelper.ToRadians(45f),
                    casterYawRadians));
            _castFlashes[1] = CreateCastFlash(
                new Vector3(
                    MathHelper.ToRadians(45f),
                    MathHelper.ToRadians(-45f),
                    casterYawRadians));
        }

        private static CastFlashState CreateCastFlash(Vector3 angleRadians) => new()
        {
            Active = true,
            AngleRadians = angleRadians,
            PreviousScale = 0.2f,
            Scale = 0.2f,
            ExpansionVelocity = 0.1f,
            Brightness = 1f,
            LifeTicks = 10
        };

        private Vector3 ResolveStartPosition()
        {
            Vector3 localOffset = new(80f, 0f, 20f);
            if (_caster is PlayerObject player && player.TryGetBoneWorldMatrix(0, out Matrix boneMatrix))
                return Vector3.Transform(localOffset, boneMatrix);

            return Vector3.Transform(localOffset, _caster.WorldPosition);
        }

        private void TickLegacy()
        {
            UpdateTrailSegments();
            UpdateCastFlashes();

            for (int streamIndex = 0; streamIndex < _streams.Length; streamIndex++)
            {
                ref StreamState stream = ref _streams[streamIndex];
                if (!stream.Active)
                    continue;

                stream.PreviousPosition = stream.Position;
                int substepCount = 0;

                // Mirrors: for (int i = 1; i < Gravity; ++i).
                for (int step = 1; step < stream.SubstepCounter; step++)
                {
                    Vector3 targetPosition = ResolveTargetPosition(stream.TargetHeightRatio);

                    if (MuGame.Random.Next(2) == 0)
                    {
                        stream.PitchDegrees = stream.PitchDegrees < -90f
                            ? stream.PitchDegrees + 20f
                            : stream.PitchDegrees - 20f;
                    }

                    float distance = MoveHoming(ref stream, targetPosition);
                    SpawnTrailSegment(stream);
                    substepCount++;

                    if (!_impactSoundPlayed && distance < 40f)
                    {
                        SoundController.Instance.PlayBuffer(ImpactSoundPath);
                        _impactSoundPlayed = true;
                    }
                }

                // A float loop counter intentionally creates one, then two, then three
                // movement/emission substeps as the original Gravity value grows.
                stream.SubstepCounter += 0.1f;
                if (stream.LifeTicks < 10)
                    stream.TurnVelocityDegrees += 0.1f;

                stream.LifeTicks--;
                if (stream.LifeTicks <= 0)
                {
                    stream.Active = false;

                    if (!_impactSoundPlayed && streamIndex == 0)
                    {
                        SoundController.Instance.PlayBuffer(ImpactSoundPath);
                        _impactSoundPlayed = true;
                    }
                }
                else if (substepCount == 0)
                {
                    // Defensive fallback for corrupted counters; the normal value starts at 2.
                    Vector3 targetPosition = ResolveTargetPosition(stream.TargetHeightRatio);
                    _ = MoveHoming(ref stream, targetPosition);
                    SpawnTrailSegment(stream);
                }
            }
        }

        private float MoveHoming(ref StreamState stream, Vector3 targetPosition)
        {
            Vector3 delta = targetPosition - stream.Position;
            float horizontalDistance = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            float distance = delta.Length();

            float desiredYaw = MathHelper.ToDegrees(MathF.Atan2(delta.X, -delta.Y));
            float desiredPitch = -MathHelper.ToDegrees(MathF.Atan2(delta.Z, MathF.Max(0.001f, horizontalDistance)));

            stream.YawDegrees = TurnTowardsDegrees(
                stream.YawDegrees,
                desiredYaw,
                stream.TurnVelocityDegrees);
            stream.PitchDegrees = TurnTowardsDegrees(
                stream.PitchDegrees,
                desiredPitch,
                stream.TurnVelocityDegrees);

            float yaw = MathHelper.ToRadians(stream.YawDegrees);
            float pitch = MathHelper.ToRadians(stream.PitchDegrees);
            float horizontal = MathF.Cos(pitch) * DirectionStep;

            Vector3 movement = new(
                MathF.Sin(yaw) * horizontal,
                -MathF.Cos(yaw) * horizontal,
                -MathF.Sin(pitch) * DirectionStep);

            stream.Position += movement;
            stream.TurnVelocityDegrees += 0.4f;
            return distance;
        }

        private Vector3 ResolveTargetPosition(float heightRatio)
        {
            if (_targetId != 0 &&
                _walkableWorld.TryGetWalkerById(_targetId, out WalkerObject target) &&
                target.Status != GameControlStatus.Disposed)
            {
                BoundingBox bounds = target.BoundingBoxWorld;
                float bottom = bounds.Min.Z;
                float top = bounds.Max.Z;
                float targetZ = MathHelper.Lerp(bottom, top, heightRatio);
                Vector3 center = target.WorldPosition.Translation;
                return new Vector3(center.X, center.Y, targetZ);
            }

            return _fallbackTargetPosition ??
                   (_caster.WorldPosition.Translation + Vector3.UnitZ * 90f);
        }

        private void SpawnTrailSegment(in StreamState stream)
        {
            int slot = FindTrailSlot();
            float ageAlpha = MathHelper.Clamp((20f - stream.LifeTicks) / 5f, 0f, 1f);
            int lifeTicks = Math.Max(1, stream.LifeTicks);

            _trailSegments[slot] = new TrailSegment
            {
                Active = true,
                Position = stream.Position,
                AngleRadians = new Vector3(
                    MathHelper.ToRadians(stream.PitchDegrees),
                    0f,
                    MathHelper.ToRadians(stream.YawDegrees)),
                Intensity = ageAlpha,
                Rotation = MathHelper.ToRadians(MuGame.Random.Next(0, 360)),
                LifeTicks = lifeTicks,
                MaxLifeTicks = lifeTicks
            };
        }

        private int FindTrailSlot()
        {
            for (int offset = 0; offset < _trailSegments.Length; offset++)
            {
                int index = (_trailWriteCursor + offset) % _trailSegments.Length;
                if (!_trailSegments[index].Active)
                {
                    _trailWriteCursor = (index + 1) % _trailSegments.Length;
                    return index;
                }
            }

            int reused = _trailWriteCursor;
            _trailWriteCursor = (_trailWriteCursor + 1) % _trailSegments.Length;
            return reused;
        }

        private void UpdateTrailSegments()
        {
            for (int i = 0; i < _trailSegments.Length; i++)
            {
                ref TrailSegment segment = ref _trailSegments[i];
                if (!segment.Active)
                    continue;

                segment.LifeTicks--;
                if (segment.LifeTicks <= 0)
                {
                    segment.Active = false;
                    continue;
                }

                if (segment.LifeTicks < 5)
                    segment.Intensity /= 1.3f;
            }
        }

        private void UpdateCastFlashes()
        {
            for (int i = 0; i < _castFlashes.Length; i++)
            {
                ref CastFlashState flash = ref _castFlashes[i];
                if (!flash.Active)
                    continue;

                flash.PreviousScale = flash.Scale;
                flash.Scale += flash.ExpansionVelocity;
                flash.ExpansionVelocity += 0.02f;
                flash.LifeTicks--;

                if (flash.LifeTicks < 7)
                    flash.Brightness /= 1.8f;

                if (flash.LifeTicks <= 0 || flash.Brightness <= 0.002f)
                    flash.Active = false;
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || Status != GameControlStatus.Ready || !_initialized)
                return;

            DrawCastFlashes();
            DrawStreams();
            DrawTrailModels();
            DrawTrailBillboards();
        }

        private void DrawCastFlashes()
        {
            if (_castFlashRenderer == null)
                return;

            for (int i = 0; i < _castFlashes.Length; i++)
            {
                ref CastFlashState flash = ref _castFlashes[i];
                if (!flash.Active)
                    continue;

                float scale = MathHelper.Lerp(
                    flash.PreviousScale,
                    flash.Scale,
                    _renderInterpolation);
                _castFlashRenderer.DrawInstance(
                    _startPosition,
                    flash.AngleRadians,
                    scale,
                    CastFlashColor * flash.Brightness,
                    flash.Brightness);
            }
        }

        private void DrawStreams()
        {
            if (_streamRenderer == null)
                return;

            for (int i = 0; i < _streams.Length; i++)
            {
                ref StreamState stream = ref _streams[i];
                if (!stream.Active)
                    continue;

                Vector3 position = Vector3.Lerp(
                    stream.PreviousPosition,
                    stream.Position,
                    _renderInterpolation);
                Vector3 angle = new(
                    MathHelper.ToRadians(stream.PitchDegrees),
                    0f,
                    MathHelper.ToRadians(stream.YawDegrees));

                _streamRenderer.DrawInstance(
                    position,
                    angle,
                    1.2f,
                    Vector3.One,
                    1f);
            }
        }

        private void DrawTrailModels()
        {
            if (_trailRenderer == null)
                return;

            for (int i = 0; i < _trailSegments.Length; i++)
            {
                ref TrailSegment segment = ref _trailSegments[i];
                if (!segment.Active || segment.Intensity <= 0.003f)
                    continue;

                float lifeRatio = segment.LifeTicks / (float)Math.Max(1, segment.MaxLifeTicks);
                float brightness = segment.Intensity * MathHelper.Clamp(lifeRatio * 1.4f, 0f, 1f);
                _trailRenderer.DrawInstance(
                    segment.Position,
                    segment.AngleRadians,
                    0.5f,
                    TrailModelColor * brightness,
                    brightness);
            }
        }

        private void DrawTrailBillboards()
        {
            if (_fireTexture == null || _fireTexture.IsDisposed || _billboardEffect == null)
                return;

            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;
            int quadCount = 0;

            for (int i = 0; i < _trailSegments.Length && quadCount < MaxBillboardQuads; i++)
            {
                ref TrailSegment segment = ref _trailSegments[i];
                if (!segment.Active || segment.Intensity <= 0.003f)
                    continue;

                float lifeRatio = segment.LifeTicks / (float)Math.Max(1, segment.MaxLifeTicks);
                float brightness = segment.Intensity * MathF.Pow(lifeRatio, 0.65f);
                if (brightness <= 0.003f)
                    continue;

                float cosine = MathF.Cos(segment.Rotation);
                float sine = MathF.Sin(segment.Rotation);
                Vector3 rotatedRight = cameraRight * cosine + cameraUp * sine;
                Vector3 rotatedUp = cameraUp * cosine - cameraRight * sine;
                float halfSize = 25f;

                WriteBillboardQuad(
                    quadCount++,
                    segment.Position,
                    rotatedRight * halfSize,
                    rotatedUp * halfSize,
                    ToAdditiveColor(FireColor * brightness));
            }

            if (quadCount == 0)
                return;

            GraphicsDevice device = GraphicsDevice;
            BlendState previousBlend = device.BlendState;
            DepthStencilState previousDepth = device.DepthStencilState;
            RasterizerState previousRasterizer = device.RasterizerState;
            SamplerState previousSampler = device.SamplerStates[0];

            try
            {
                device.BlendState = Blendings.OneOneAdditive;
                device.DepthStencilState = DepthStencilState.DepthRead;
                device.RasterizerState = RasterizerState.CullNone;
                device.SamplerStates[0] = SamplerState.LinearClamp;

                _billboardEffect.Texture = _fireTexture;
                _billboardEffect.World = Matrix.Identity;
                _billboardEffect.View = Camera.Instance.View;
                _billboardEffect.Projection = Camera.Instance.Projection;
                _billboardEffect.DiffuseColor = Vector3.One;
                _billboardEffect.Alpha = 1f;

                foreach (EffectPass pass in _billboardEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    device.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        _billboardVertices,
                        0,
                        quadCount * 4,
                        BillboardIndices,
                        0,
                        quadCount * 2);
                }
            }
            finally
            {
                device.BlendState = previousBlend;
                device.DepthStencilState = previousDepth;
                device.RasterizerState = previousRasterizer;
                device.SamplerStates[0] = previousSampler;
            }
        }

        private void WriteBillboardQuad(
            int quadIndex,
            Vector3 center,
            Vector3 right,
            Vector3 up,
            Color color)
        {
            int vertex = quadIndex * 4;
            _billboardVertices[vertex] = new VertexPositionColorTexture(
                center - right - up,
                color,
                new Vector2(0f, 1f));
            _billboardVertices[vertex + 1] = new VertexPositionColorTexture(
                center + right - up,
                color,
                new Vector2(1f, 1f));
            _billboardVertices[vertex + 2] = new VertexPositionColorTexture(
                center + right + up,
                color,
                new Vector2(1f, 0f));
            _billboardVertices[vertex + 3] = new VertexPositionColorTexture(
                center - right + up,
                color,
                new Vector2(0f, 0f));
        }



        private bool HasActiveContent()
        {
            for (int i = 0; i < _streams.Length; i++)
                if (_streams[i].Active)
                    return true;

            for (int i = 0; i < _trailSegments.Length; i++)
                if (_trailSegments[i].Active)
                    return true;

            for (int i = 0; i < _castFlashes.Length; i++)
                if (_castFlashes[i].Active)
                    return true;

            return false;
        }

        private void RemoveSelf()
        {
            if (_disposed)
                return;

            World?.RemoveObject(this);
            Dispose();
        }

        private static float TurnTowardsDegrees(float current, float target, float maxDelta)
        {
            float delta = NormalizeDegrees(target - current);
            delta = MathHelper.Clamp(delta, -maxDelta, maxDelta);
            return NormalizeDegrees(current + delta);
        }

        private static float NormalizeDegrees(float value)
        {
            value %= 360f;
            if (value > 180f)
                value -= 360f;
            else if (value < -180f)
                value += 360f;
            return value;
        }

        private static Color ToAdditiveColor(Vector3 color)
        {
            color = Vector3.Clamp(color, Vector3.Zero, Vector3.One);
            return new Color(color.X, color.Y, color.Z, 1f);
        }

        private static async Task<string> ResolveModelPath(string primary, params string[] candidates)
        {
            if (await BMDLoader.Instance.AssestExist(primary))
                return primary;

            for (int i = 0; i < candidates.Length; i++)
            {
                if (await BMDLoader.Instance.AssestExist(candidates[i]))
                    return candidates[i];
            }

            return primary;
        }

        private static async Task<Texture2D?> PrepareFirstTexture(params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    Texture2D? texture = await TextureLoader.Instance.PrepareAndGetTexture(candidates[i]);
                    if (texture != null)
                        return texture;
                }
                catch
                {
                    // Missing optional fire billboard: keep the model segments and skip the quad.
                }
            }

            return null;
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _streamRenderer?.Dispose();
            _trailRenderer?.Dispose();
            _castFlashRenderer?.Dispose();
            _billboardEffect?.Dispose();
            _streamRenderer = null;
            _trailRenderer = null;
            _castFlashRenderer = null;
            _billboardEffect = null;
            _fireTexture = null;
            base.Dispose();
        }

        private sealed class SharedModelRenderer : ModelObject
        {
            private readonly string _modelPath;

            public SharedModelRenderer(string modelPath, int hiddenMesh, int blendMesh)
            {
                _modelPath = modelPath;
                HiddenMesh = hiddenMesh;
                BlendMesh = blendMesh;
                ContinuousAnimation = true;
                AnimationSpeed = 1f;
                LightEnabled = false;
                RenderShadow = false;
                IsTransparent = true;
                AffectedByTransparency = true;
                DepthState = DepthStencilState.DepthRead;
                BlendState = Blendings.OneOneAdditive;
                BlendMeshState = Blendings.OneOneAdditive;
                BlendMeshLight = 1f;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_modelPath);
                await base.Load();
            }

            public void DrawInstance(
                Vector3 position,
                Vector3 angleRadians,
                float scale,
                Vector3 light,
                float brightness)
            {
                if (Status != GameControlStatus.Ready || Model == null)
                    return;

                Position = position;
                Angle = angleRadians;
                Scale = scale;
                Light = light;
                BlendMeshLight = brightness;

                GraphicsDevice device = GraphicsDevice;
                RasterizerState previousRasterizer = device.RasterizerState;
                try
                {
                    device.RasterizerState = RasterizerState.CullNone;
                    GraphicsManager.Instance.AlphaTestEffect3D.View = Camera.Instance.View;
                    GraphicsManager.Instance.AlphaTestEffect3D.Projection = Camera.Instance.Projection;
                    GraphicsManager.Instance.AlphaTestEffect3D.World = WorldPosition;
                    DrawModel(true);
                }
                finally
                {
                    device.RasterizerState = previousRasterizer;
                }
            }
        }
    }
}
