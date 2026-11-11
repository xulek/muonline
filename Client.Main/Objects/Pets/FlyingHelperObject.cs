#nullable enable
using Client.Data.BMD;
using Client.Data.ATT;
using Client.Main.Content;
using Client.Main.Controls;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Objects.Pets
{
    public enum FlyingHelperKind
    {
        None = -1,
        GuardianAngel = 0,
        Imp = 1,
        DarkRaven = 2
    }

    public enum DarkRavenCommandMode
    {
        Follow = 0,
        AutoAttack = 1,
        AttackWithCharacter = 2,
        AttackTarget = 3
    }

    public enum DarkRavenAttackKind
    {
        SingleTarget = 0,
        Range = 1
    }

    /// <summary>
    /// Guardian Angel flies independently around its owner. Imp keeps its own animated BMD pose,
    /// but follows the player's right shoulder. Dark Raven follows the original DarkSpirit
    /// flight/stand state machine and emits its blue bone sparks.
    /// </summary>
    public sealed class FlyingHelperObject : ModelObject
    {
        private const string GuardianModelPath = "Player/Helper01.bmd";
        private const string ImpModelPath = "Player/Helper02.bmd";
        private const string DarkRavenModelPath = "Skill/DarkSpirit.bmd";
        private const string DarkRavenRushSoundPath = "Sound/DSpirit_Rush.wav";
        private const string DarkRavenMissileSoundPath = "Sound/DSpirit_Missile.wav";
        private const string SparkTexturePath = "Effect/Spark01.jpg";
        private const string LightTexturePath = "Effect/flare01.jpg";

        private const float LegacyStepSeconds = 1f / 25f;
        // ModelObject advances BMD frames in real-time units. The previous 12.5 fps port was
        // visibly 2-3x faster than the classic helper wing cycle, so keep the helper animation
        // at a deliberately slower 5 frames per second.
        private const float HelperWingAnimationSpeed = 5f;
        private const float GuardianWorldScale = 0.65f;
        private const float GuardianCharacterSceneScale = 0.78f;
        private const float ImpWorldScale = 0.5f;
        private const float ImpCharacterSceneScale = 0.6f;
        // SourceMain's CreatePetPointer initializes DarkSpirit at scale 0.7.
        private const float DarkRavenWorldScale = 0.7f;
        private const float DarkRavenCharacterSceneScale = 0.7f;
        private const float DarkRavenAnimationSpeed = 10f;
        private const float DarkRavenStartHeight = 300f;
        private const float DarkRavenFlightHeight = 250f;
        private const float DarkRavenStandAngleOffset = -2.0943952f;
        private const float DarkRavenSkillSpinRadiansPerFrame = 0.7853982f;
        private const int DarkRavenStandBoneIndex = 37;
        private const int DarkRavenSparkBoneCount = 66;
        private const int DarkRavenAttackBoneCount = 66;
        private const ushort NoDarkRavenTarget = 0xFFFF;
        private const int MaxLegacyStepsPerFrame = 5;
        private const float FlyRange = 150f;
        private const int MaxSparks = 128;
        private const int MaxBillboardQuads = MaxSparks + 1;

        private readonly Random _random = new();
        private readonly SemaphoreSlim _modelApplyGate = new(1, 1);
        private readonly SparkParticle[] _sparks = new SparkParticle[MaxSparks];
        private bool _sparksUsed;
        private readonly VertexPositionColorTexture[] _billboardVertices =
            new VertexPositionColorTexture[MaxBillboardQuads * 4];
        private static readonly short[] BillboardIndices = QuadIndexCache.Get(MaxBillboardQuads);

        private FlyingHelperKind _kind = FlyingHelperKind.None;
        private int _modelRequestVersion;
        private bool _spawnInitialized;
        private bool _modelContentReady;
        private bool _hasPendingKind;
        private FlyingHelperKind _pendingKind;
        private Vector3 _previousSimulationPosition;
        private Vector3 _simulationPosition;
        private float _simulationYaw;
        private float _localSpeed;
        private float _verticalSpeed;
        private float _legacyAccumulator;
        private DarkRavenMotion _darkRavenMotion = DarkRavenMotion.Flying;
        private float _darkRavenLandingSpeed;
        private DarkRavenCommandMode _darkRavenCommandMode = DarkRavenCommandMode.Follow;
        private ushort _darkRavenCommandTargetId = NoDarkRavenTarget;
        private DarkRavenAttackKind? _darkRavenAttackKind;
        private ushort _darkRavenAttackTargetId = NoDarkRavenTarget;
        private int _darkRavenAttackTime;
        private bool _darkRavenAttackFlashSpawned;

        private const int ImpLinkBoneIndex = 34;
        private static readonly Vector3 ImpLinkOffset = new(20f, 0f, 0f);

        private BasicEffect? _billboardEffect;
        private Texture2D? _sparkTexture;
        private Texture2D? _lightTexture;
        private bool _ownsSparkTexture;
        private bool _ownsLightTexture;

        public FlyingHelperKind Kind => _kind;
        public DarkRavenCommandMode CommandMode => _darkRavenCommandMode;
        public ushort CommandTargetId => _darkRavenCommandTargetId;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool PreserveBlendMeshesInLowQuality => true;
        // Helper01/02 are legacy flying-effect models. The inventory preview renders them
        // correctly with CPU geometry and culling disabled, while the generic world GPU/culling
        // path can reject their entire mesh. Keep these two models on the compatibility path.
        protected override bool AllowGpuSkinning => false;
        protected override bool AllowDynamicLightingShader => false;
        protected override bool ForceTwoSidedMeshes => true;

        protected override bool ShouldRenderMesh(int meshIndex)
        {
            // SourceMain renders DarkSpirit's base mesh (2) together with its effect mesh (3)
            // for the default pet level. Keep alternate data packs with fewer meshes usable.
            if (_kind != FlyingHelperKind.DarkRaven || Model?.Meshes == null || Model.Meshes.Length < 4)
                return true;

            return meshIndex == 2 || meshIndex == 3;
        }

        public FlyingHelperObject()
        {
            Hidden = true;
            RenderShadow = false;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.AlphaBlend;
            BlendMeshState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BlendMesh = -1;
            Alpha = 0f;
            Light = new Vector3(3f, 3f, 3f);
            LightEnabled = true;
            Scale = ImpWorldScale;
            CurrentAction = 0;
            AnimationSpeed = HelperWingAnimationSpeed;
            ContinuousAnimation = true;
            LinkParentAnimation = false;
            ParentBoneLink = -1;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-80f, -80f, -80f),
                new Vector3(80f, 80f, 120f));

        }

        public void SetDarkRavenCommand(DarkRavenCommandMode mode, ushort targetId)
        {
            if (_kind != FlyingHelperKind.DarkRaven ||
                (int)mode < (int)DarkRavenCommandMode.Follow ||
                (int)mode > (int)DarkRavenCommandMode.AttackTarget)
            {
                return;
            }

            _darkRavenCommandMode = mode;
            _darkRavenCommandTargetId = mode == DarkRavenCommandMode.AttackTarget
                ? targetId
                : NoDarkRavenTarget;
        }

        public void StartDarkRavenAttack(DarkRavenAttackKind attackKind, ushort targetId)
        {
            if (_kind != FlyingHelperKind.DarkRaven ||
                Status != GameControlStatus.Ready ||
                _darkRavenMotion != DarkRavenMotion.Flying)
            {
                return;
            }

            _darkRavenAttackKind = attackKind;
            _darkRavenAttackTargetId = targetId;
            _darkRavenAttackTime = 0;
            _darkRavenAttackFlashSpawned = false;
            SetDarkRavenAnimation(3);
            SoundController.Instance.PlayBuffer(
                attackKind == DarkRavenAttackKind.Range
                    ? DarkRavenMissileSoundPath
                    : DarkRavenRushSoundPath);
        }

        public override async Task Load()
        {
            await base.Load();

            // ModelObject.LoadContent() treats a null model as a valid ready state. Equipment
            // updates can arrive while the outer WorldObject.Load() is still in progress, so the
            // helper kind must be applied only after the complete child lifecycle has finished.
            if (Status == GameControlStatus.Ready && _hasPendingKind)
            {
                FlyingHelperKind pendingKind = _pendingKind;
                _hasPendingKind = false;
                await SetKindAsync(pendingKind);
            }
        }

        public async Task SetKindAsync(FlyingHelperKind kind)
        {
            int requestVersion = Interlocked.Increment(ref _modelRequestVersion);

            if (IsDisposeRequested || Status == GameControlStatus.Disposed)
                return;

            // Parent and child content loads run concurrently. Defer equipment changes until the
            // complete WorldObject.Load() has finished, not merely until a null-model content pass
            // temporarily reports Ready.
            if ((Status is GameControlStatus.NonInitialized or GameControlStatus.Initializing) ||
                IsLoadInProgress)
            {
                _pendingKind = kind;
                _hasPendingKind = true;
                return;
            }

            _hasPendingKind = false;

            BMD? model = null;
            if (kind != FlyingHelperKind.None)
            {
                if (_kind == kind && Model != null && _modelContentReady)
                    return;

                string primaryPath = kind switch
                {
                    FlyingHelperKind.GuardianAngel => GuardianModelPath,
                    FlyingHelperKind.Imp => ImpModelPath,
                    FlyingHelperKind.DarkRaven => DarkRavenModelPath,
                    _ => string.Empty
                };

                model = await PrepareModelWithFallbackAsync(primaryPath, kind);
                if (requestVersion != Volatile.Read(ref _modelRequestVersion))
                    return;
            }

            await ApplyResolvedKindOnMainThreadAsync(kind, model, requestVersion);
        }

        private Task ApplyResolvedKindOnMainThreadAsync(
            FlyingHelperKind kind,
            BMD? model,
            int requestVersion)
        {
            if (MuGame.IsMainThread)
                return ApplyResolvedKindAsync(kind, model, requestVersion);

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            MuGame.ScheduleOnMainThread(async () =>
            {
                try
                {
                    await ApplyResolvedKindAsync(kind, model, requestVersion);
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }, MainThreadDispatcher.WorkPriority.High, "FlyingHelper.ApplyModel");

            return completion.Task;
        }

        private async Task ApplyResolvedKindAsync(
            FlyingHelperKind requestedKind,
            BMD? resolvedModel,
            int requestVersion)
        {
            await _modelApplyGate.WaitAsync();
            try
            {
                if (requestVersion != Volatile.Read(ref _modelRequestVersion) ||
                    IsDisposeRequested || Status == GameControlStatus.Disposed)
                {
                    return;
                }

                // Assigning Model while Ready starts an unobserved fire-and-forget reload in the
                // base property setter. Put the helper into Initializing and perform exactly one
                // awaited model-content pass, so meshes, textures, bone matrices and buffers are
                // ready before the object becomes visible.
                Status = GameControlStatus.Initializing;
                Hidden = true;
                _modelContentReady = false;
                _spawnInitialized = false;
                ClearSparks();
                ClearDarkRavenAttack();

                _kind = resolvedModel == null ? FlyingHelperKind.None : requestedKind;
                Model = resolvedModel;
                BlendMesh = _kind == FlyingHelperKind.GuardianAngel ? 1 : -1;
                BlendMeshLight = 1f;
                // SourceMain's DarkSpirit pointer explicitly disables its cast shadow.
                RenderShadow = false;
                Alpha = 0f;
                CurrentAction = 0;
                AnimationSpeed = HelperWingAnimationSpeed;
                Scale = GetWorldScale(requestedKind);
                _darkRavenMotion = DarkRavenMotion.Flying;
                _darkRavenCommandMode = DarkRavenCommandMode.Follow;
                _darkRavenCommandTargetId = NoDarkRavenTarget;
                Light = _kind == FlyingHelperKind.DarkRaven
                    ? new Vector3(0.5f, 0.5f, 0.5f)
                    : new Vector3(3f, 3f, 3f);

                // Load only the ModelObject content here. Billboard resources belong to the helper
                // itself and were already initialized by the normal child Load() lifecycle.
                await base.LoadContent();
                ConfigureWingAnimation();

                bool stillCurrent = requestVersion == Volatile.Read(ref _modelRequestVersion);
                _modelContentReady = stillCurrent && resolvedModel != null && Model == resolvedModel;
                Hidden = !_modelContentReady;
                Status = GameControlStatus.Ready;
            }
            catch
            {
                _kind = FlyingHelperKind.None;
                _modelContentReady = false;
                _spawnInitialized = false;
                Hidden = true;
                Model = null;
                Status = GameControlStatus.Error;
                throw;
            }
            finally
            {
                _modelApplyGate.Release();
            }

            // A newer equipment update may have arrived while this model was loading. Apply only
            // the final requested helper after the current content pass has fully completed.
            if (_hasPendingKind && Status == GameControlStatus.Ready)
            {
                FlyingHelperKind pendingKind = _pendingKind;
                _hasPendingKind = false;
                await SetKindAsync(pendingKind);
            }
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (_sparkTexture == null || _sparkTexture.IsDisposed)
            {
                _sparkTexture = await PrepareTextureAsync(SparkTexturePath);
                if (_sparkTexture == null)
                {
                    await MuGame.YieldToNextFrameAsync("FlyingHelper.SparkTexture");
                    _sparkTexture = CreateRadialTexture(GraphicsDevice, 32, 2.8f);
                    _ownsSparkTexture = true;
                }
            }

            if (_lightTexture == null || _lightTexture.IsDisposed)
            {
                _lightTexture = await PrepareTextureAsync(LightTexturePath);
                if (_lightTexture == null)
                {
                    await MuGame.YieldToNextFrameAsync("FlyingHelper.LightTexture");
                    _lightTexture = CreateRadialTexture(GraphicsDevice, 64, 2.2f);
                    _ownsLightTexture = true;
                }
            }

            if (_billboardEffect == null && !MuGame.IsMainThread)
                await MuGame.YieldToNextFrameAsync("FlyingHelper.BillboardEffect");

            _billboardEffect ??= new BasicEffect(GraphicsDevice)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
                FogEnabled = false,
                World = Matrix.Identity
            };
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || Parent is not PlayerObject owner)
                return;

            bool blocked = _kind == FlyingHelperKind.None ||
                           Model == null ||
                           !_modelContentReady ||
                           owner.Hidden ||
                           owner.World == null ||
                           IsChaosCastleMap(owner.World.WorldIndex);

            if (blocked)
            {
                Hidden = true;
                _spawnInitialized = false;
                ClearSparks();
                return;
            }

            if (!_spawnInitialized)
                InitializeSpawn(owner);

            Hidden = false;
            bool characterScene = MuGame.Instance.ActiveScene is SelectCharacterScene;
            Scale = characterScene
                ? GetCharacterSceneScale(_kind)
                : GetWorldScale(_kind);

            float elapsed = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                LegacyStepSeconds * MaxLegacyStepsPerFrame);

            _legacyAccumulator += elapsed;
            int steps = 0;
            while (_legacyAccumulator >= LegacyStepSeconds && steps < MaxLegacyStepsPerFrame)
            {
                TickLegacy(owner);
                _legacyAccumulator -= LegacyStepSeconds;
                steps++;
            }

            if (steps == MaxLegacyStepsPerFrame)
                _legacyAccumulator = MathF.Min(_legacyAccumulator, LegacyStepSeconds);

            if (_kind == FlyingHelperKind.Imp)
            {
                UpdateImpShoulderTransform(owner);
            }
            else if (_kind == FlyingHelperKind.DarkRaven)
            {
                UpdateDarkRavenTransform(owner);
            }
            else
            {
                float interpolation = MathHelper.Clamp(_legacyAccumulator / LegacyStepSeconds, 0f, 1f);
                Position = Vector3.Lerp(_previousSimulationPosition, _simulationPosition, interpolation);
                Angle = new Vector3(0f, 0f, _simulationYaw);
            }

            base.Update(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || (_kind != FlyingHelperKind.GuardianAngel && _kind != FlyingHelperKind.DarkRaven) ||
                _billboardEffect == null || Parent is not PlayerObject owner)
            {
                return;
            }

            float inheritedAlpha = MathHelper.Clamp(TotalAlpha, 0f, 1f);
            if (inheritedAlpha <= 0.01f)
                return;

            BlendState previousBlend = GraphicsDevice.BlendState;
            DepthStencilState previousDepth = GraphicsDevice.DepthStencilState;
            RasterizerState previousRasterizer = GraphicsDevice.RasterizerState;
            SamplerState previousSampler = GraphicsDevice.SamplerStates[0];

            try
            {
                GraphicsDevice.BlendState = Blendings.OneOneAdditive;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                _billboardEffect.World = Matrix.Identity;
                _billboardEffect.View = Camera.Instance.View;
                _billboardEffect.Projection = Camera.Instance.Projection;

                if (_kind == FlyingHelperKind.GuardianAngel)
                {
                    DrawGuardianGlow(inheritedAlpha);

                    // The original suppresses spark emission during cloaking, while still requesting
                    // the helper glow. TotalAlpha is the available equivalent of the cloak render state.
                    if (owner.TotalAlpha >= 0.8f)
                        DrawGuardianSparks(inheritedAlpha);
                }
                else if (owner.TotalAlpha >= 0.8f)
                {
                    DrawDarkRavenSparks(inheritedAlpha);
                }
            }
            finally
            {
                GraphicsDevice.BlendState = previousBlend;
                GraphicsDevice.DepthStencilState = previousDepth;
                GraphicsDevice.RasterizerState = previousRasterizer;
                GraphicsDevice.SamplerStates[0] = previousSampler;
            }
        }

        protected override void RecalculateWorldPosition()
        {
            // Position is already expressed in world coordinates. Guardian uses its simulated
            // position, while Imp receives a shoulder position sampled from the player's current
            // bone palette. The helper still uses its own bones for wing animation.
            WorldPosition = Matrix.CreateScale(Scale)
                * Matrix.CreateFromQuaternion(Client.Main.Core.Utilities.MathUtils.AngleQuaternion(Angle))
                * Matrix.CreateTranslation(Position);
        }

        private void InitializeSpawn(PlayerObject owner)
        {
            Vector3 ownerPosition = owner.WorldPosition.Translation;
            Vector3 spawn;

            if (_kind == FlyingHelperKind.GuardianAngel)
            {
                spawn = new Vector3(
                    ownerPosition.X + _random.Next(-256, 256),
                    ownerPosition.Y + _random.Next(-256, 256),
                    ownerPosition.Z + _random.Next(128, 256));
            }
            else if (_kind == FlyingHelperKind.DarkRaven)
            {
                spawn = ownerPosition + new Vector3(0f, 0f, DarkRavenStartHeight);
                _darkRavenMotion = DarkRavenMotion.Flying;
            }
            else
            {
                spawn = GetImpShoulderPosition(owner);
            }

            _previousSimulationPosition = spawn;
            _simulationPosition = spawn;
            _simulationYaw = owner.Angle.Z;
            _localSpeed = 0f;
            _verticalSpeed = 0f;
            _legacyAccumulator = 0f;
            Alpha = 0f;
            BlendMeshLight = 1f;
            // ModelObject's CPU path bakes TotalAlpha into vertex lighting. A reused helper may
            // still have buffers created at full alpha, so explicitly rebuild them for fade-in.
            InvalidateBuffers(BufferFlagLighting | BufferFlagMaterial);
            Position = spawn;
            Angle = new Vector3(0f, 0f, _simulationYaw);
            ClearSparks();
            _spawnInitialized = true;
        }

        private void TickLegacy(PlayerObject owner)
        {
            _previousSimulationPosition = _simulationPosition;

            float previousAlpha = Alpha;
            Alpha += (1f - Alpha) * 0.1f;
            if (MathF.Abs(Alpha - previousAlpha) > 0.0001f)
            {
                // Non-blend CPU meshes store alpha in their vertex RGB lighting. Without this
                // invalidation Helper01/02 are built once at Alpha=0 and remain black forever,
                // even though the independently rendered Guardian billboard fades in correctly.
                InvalidateBuffers(BufferFlagLighting | BufferFlagMaterial);
            }

            if (BlendMeshLight > Alpha)
                BlendMeshLight = Alpha;

            if (_kind == FlyingHelperKind.Imp)
            {
                Vector3 shoulder = GetImpShoulderPosition(owner);
                _previousSimulationPosition = shoulder;
                _simulationPosition = shoulder;
                _simulationYaw = owner.Angle.Z;
                _localSpeed = 0f;
                _verticalSpeed = 0f;
                ClearSparks();
                return;
            }

            if (_kind == FlyingHelperKind.DarkRaven)
            {
                TickDarkRaven(owner);
                return;
            }

            Vector3 ownerPosition = owner.WorldPosition.Translation;
            Vector2 delta = new(
                ownerPosition.X - _simulationPosition.X,
                ownerPosition.Y - _simulationPosition.Y);
            float distanceSquared = delta.LengthSquared();

            if (distanceSquared >= FlyRange * FlyRange)
            {
                float targetYaw = MathF.Atan2(delta.X, -delta.Y);
                _simulationYaw = TurnTowards(_simulationYaw, targetYaw, MathHelper.ToRadians(20f));
            }

            float forwardX = MathF.Sin(_simulationYaw);
            float forwardY = -MathF.Cos(_simulationYaw);
            _simulationPosition.X += forwardX * -_localSpeed;
            _simulationPosition.Y += forwardY * -_localSpeed;
            _simulationPosition.Z += _verticalSpeed;
            _simulationPosition.Z += _random.Next(-8, 8);

            if (_random.Next(32) == 0)
            {
                if (distanceSquared >= FlyRange * FlyRange)
                {
                    _localSpeed = -(_random.Next(128, 192) * 0.1f);
                }
                else
                {
                    _localSpeed = -(_random.Next(16, 80) * 0.1f);
                    _simulationYaw = MathHelper.ToRadians(_random.Next(0, 360));
                }

                _verticalSpeed = _random.Next(-32, 32) * 0.1f;
            }

            if (_simulationPosition.Z < ownerPosition.Z + 100f)
                _verticalSpeed += 1.5f;
            if (_simulationPosition.Z > ownerPosition.Z + 200f)
                _verticalSpeed -= 1.5f;

            if (_kind == FlyingHelperKind.GuardianAngel && owner.TotalAlpha >= 0.8f)
            {
                for (int i = 0; i < 4; i++)
                    SpawnSpark();
            }

            UpdateSparks();
        }

        private void TickDarkRaven(PlayerObject owner)
        {
            if (IsOwnerInSafeZone(owner))
            {
                ClearDarkRavenAttack();
                _localSpeed = 0f;
                _verticalSpeed = 0f;
                ClearSparks();

                if (_darkRavenMotion == DarkRavenMotion.Flying)
                {
                    _darkRavenMotion = DarkRavenMotion.Landing;
                    _darkRavenLandingSpeed = 3f;
                }

                if (_darkRavenMotion == DarkRavenMotion.Landing)
                {
                    TickDarkRavenLanding(owner);
                    return;
                }

                SetDarkRavenAnimation(2);
                return;
            }

            if (_darkRavenMotion == DarkRavenMotion.Standing ||
                _darkRavenMotion == DarkRavenMotion.Landing)
            {
                _darkRavenMotion = DarkRavenMotion.Flying;
                Vector3 ownerPosition = owner.WorldPosition.Translation;
                _previousSimulationPosition = _simulationPosition =
                    ownerPosition + new Vector3(0f, 0f, DarkRavenFlightHeight);
                _simulationYaw = owner.Angle.Z;
            }

            if (_darkRavenAttackKind.HasValue)
            {
                TickDarkRavenAttack(owner);
                return;
            }

            Vector3 currentOwnerPosition = owner.WorldPosition.Translation;
            Vector2 delta = new(
                currentOwnerPosition.X - _simulationPosition.X,
                currentOwnerPosition.Y - _simulationPosition.Y);
            float distanceSquared = delta.LengthSquared();

            if (distanceSquared >= FlyRange * FlyRange)
            {
                float targetYaw = MathF.Atan2(delta.X, -delta.Y);
                _simulationYaw = TurnTowards(_simulationYaw, targetYaw, MathHelper.ToRadians(20f));
            }

            float forwardX = MathF.Sin(_simulationYaw);
            float forwardY = -MathF.Cos(_simulationYaw);
            _simulationPosition.X += forwardX * -_localSpeed;
            _simulationPosition.Y += forwardY * -_localSpeed;
            _simulationPosition.Z += _verticalSpeed;
            _simulationPosition.Z += _random.Next(-32, 32) * 0.1f;

            if (_random.Next(32) == 0)
            {
                if (distanceSquared >= FlyRange * FlyRange)
                {
                    _localSpeed = -(_random.Next(128, 192) * 0.1f);
                }
                else
                {
                    _localSpeed = -(_random.Next(16, 80) * 0.1f);
                    _simulationYaw = MathHelper.ToRadians(_random.Next(0, 360));
                }

                _verticalSpeed = _random.Next(-32, 32) * 0.1f;
            }

            float targetHeight = currentOwnerPosition.Z + DarkRavenFlightHeight;
            if (_simulationPosition.Z < targetHeight - 25f)
                _verticalSpeed += 1.5f;
            if (_simulationPosition.Z > targetHeight + 25f)
                _verticalSpeed -= 1.5f;

            SetDarkRavenAnimation(_localSpeed <= -12f ? 1 : 0);

            // SourceMain emits one blue spark from a random DarkSpirit bone per legacy tick.
            if (owner.TotalAlpha >= 0.8f)
                SpawnSpark();

            UpdateSparks();
        }

        private void TickDarkRavenLanding(PlayerObject owner)
        {
            if (!owner.TryGetBoneWorldMatrix(PlayerObject.RightHandBoneIndex, out Matrix handMatrix))
            {
                _darkRavenMotion = DarkRavenMotion.Standing;
                SetDarkRavenAnimation(2);
                return;
            }

            Vector3 handPosition = handMatrix.Translation;
            Vector3 toHand = handPosition - _simulationPosition;
            float distance = toHand.Length();
            if (distance < 50f)
            {
                _simulationPosition = handPosition;
                _darkRavenMotion = DarkRavenMotion.Standing;
                _darkRavenLandingSpeed = 0f;
                SetDarkRavenAnimation(2);
                return;
            }

            _simulationYaw = MathF.Atan2(toHand.X, -toHand.Y);
            _simulationPosition += toHand / distance * MathF.Min(distance, _darkRavenLandingSpeed);
            _darkRavenLandingSpeed += 1f;
            SetDarkRavenAnimation(1);
        }

        private void TickDarkRavenAttack(PlayerObject owner)
        {
            DarkRavenAttackKind attackKind = _darkRavenAttackKind!.Value;
            WalkerObject target = null;
            if (_darkRavenAttackTargetId != NoDarkRavenTarget &&
                owner.World is WalkableWorldControl world)
            {
                ushort maskedTargetId = (ushort)(_darkRavenAttackTargetId & 0x7FFF);
                world.TryGetWalkerById(maskedTargetId, out target);
            }

            Vector3 targetPosition = target?.WorldPosition.Translation ?? _simulationPosition;
            Vector3 movementTarget = targetPosition;
            if (attackKind == DarkRavenAttackKind.SingleTarget && target != null)
                movementTarget.Z += 50f;

            Vector3 toTarget = movementTarget - _simulationPosition;
            float distance = toTarget.Length();

            if (distance > 0.01f)
                _simulationYaw = MathF.Atan2(toTarget.X, -toTarget.Y);

            // SourceMain's physical attack advances the raven toward its target while the
            // attack action is active. The range attack stays in place and only turns toward it.
            if (attackKind == DarkRavenAttackKind.SingleTarget &&
                distance > 20f &&
                _darkRavenAttackTime < 20)
            {
                _simulationPosition += toTarget / distance * MathF.Min(24f, distance);
            }

            distance = Vector3.Distance(movementTarget, _simulationPosition);

            SetDarkRavenAnimation(3);

            if (attackKind == DarkRavenAttackKind.SingleTarget)
            {
                if (target != null && _darkRavenAttackTime <= 2)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        SpawnDarkRavenAttackLight(
                            _simulationPosition + new Vector3(
                                _random.Next(-20, 21),
                                _random.Next(-20, 21),
                                _random.Next(-8, 25)),
                            new Vector3(1f, 0.8f, 0.6f),
                            0.18f);
                    }

                    if (!_darkRavenAttackFlashSpawned)
                    {
                        SpawnDarkRavenModelEffect(
                            "Skill/darklordskill.bmd",
                            "Skill/DarkLordSkill.bmd",
                            _simulationPosition,
                            new Vector3(
                                MathHelper.ToRadians(45f),
                                MathHelper.ToRadians(_random.Next(-90, 91)),
                                0f),
                            new Vector3(1f, 0.8f, 0.6f),
                            0.2f,
                            -2,
                            10f,
                            growthPerFrame: 0.1f,
                            growthAcceleration: 0.04f,
                            fadeBase: 1f / 1.8f,
                            fadeStartFrame: 3f);
                        _darkRavenAttackFlashSpawned = true;
                    }
                }

                if (_darkRavenAttackTime > 3 &&
                    _darkRavenAttackTime % 2 == 1 &&
                    distance > 20f &&
                    _simulationPosition.Z > owner.WorldPosition.Translation.Z + 100f)
                {
                    Vector3 airForcePosition = GetDarkRavenBoneWorldPosition(
                        6,
                        new Vector3(50f, 0f, 0f),
                        _simulationPosition);
                    SpawnDarkRavenModelEffect(
                        "Skill/AirForce.bmd",
                        "Skill/airforce.bmd",
                        airForcePosition,
                        new Vector3(0f, 0f, _simulationYaw),
                        Light,
                        0.6f,
                        -2,
                        15f,
                        growthPerFrame: 0.2f,
                        fadeBase: 1f / 1.5f);
                }
            }
            else
            {
                if (_darkRavenAttackTime < 15)
                    SpawnDarkRavenMagicAttackLights();
                else if (target != null && _darkRavenAttackTime == 15)
                    SpawnDarkRavenMagicImpact(targetPosition);
            }

            _darkRavenAttackTime++;
            int attackLife = attackKind == DarkRavenAttackKind.Range ? 16 : 22;
            if (_darkRavenAttackTime >= attackLife)
            {
                ClearDarkRavenAttack();
                _darkRavenMotion = DarkRavenMotion.Flying;
                SetDarkRavenAnimation(1);
            }

            UpdateSparks();
        }

        private void UpdateDarkRavenTransform(PlayerObject owner)
        {
            if (_darkRavenMotion == DarkRavenMotion.Standing)
            {
                Position = GetDarkRavenStandPosition(owner);
                Vector3 angle = owner.Angle;
                angle.Z += DarkRavenStandAngleOffset;
                Angle = angle;
                return;
            }

            float interpolation = MathHelper.Clamp(_legacyAccumulator / LegacyStepSeconds, 0f, 1f);
            Position = Vector3.Lerp(
                _previousSimulationPosition,
                _simulationPosition,
                interpolation);
            Angle = new Vector3(0f, 0f, _simulationYaw);
        }

        private Vector3 GetDarkRavenStandPosition(PlayerObject owner)
        {
            if (owner.TryGetBoneWorldMatrix(DarkRavenStandBoneIndex, out Matrix boneMatrix))
            {
                return Vector3.Transform(new Vector3(-10f, 0f, 10f), boneMatrix);
            }

            float bodyHeight = owner.BodyHeight > 1f ? owner.BodyHeight * 0.72f : 110f;
            return owner.WorldPosition.Translation + new Vector3(0f, 0f, bodyHeight);
        }

        private static bool IsOwnerInSafeZone(PlayerObject owner)
        {
            return owner.World is WalkableWorldControl world &&
                   world.Terrain.RequestTerrainFlag((int)owner.Location.X, (int)owner.Location.Y)
                       .HasFlag(TWFlags.SafeZone);
        }

        private void SetDarkRavenAnimation(int action)
        {
            int resolvedAction = action;
            if (Model?.Actions == null ||
                action < 0 || action >= Model.Actions.Length ||
                Model.Actions[action] == null)
            {
                resolvedAction = ResolveWingAnimationAction();
            }

            if (CurrentAction == resolvedAction)
                return;

            CurrentAction = resolvedAction;
            InvalidateBuffers(BufferFlagAnimation);
        }

        /// <summary>
        /// Rebinds an equipped helper after the persistent player object is moved to another
        /// WorldControl. Map changes intentionally retain the local player, so no equipment packet
        /// is guaranteed to arrive and recreate the helper.
        /// </summary>
        public async Task RestoreAfterWorldChangeAsync(FlyingHelperKind equippedKind)
        {
            if (IsDisposeRequested || Status == GameControlStatus.Disposed)
                return;

            if (equippedKind != FlyingHelperKind.None &&
                (_kind != equippedKind || Model == null || !_modelContentReady ||
                 Status != GameControlStatus.Ready))
            {
                await SetKindAsync(equippedKind);
            }
            else if (equippedKind == FlyingHelperKind.None && _kind != FlyingHelperKind.None)
            {
                await SetKindAsync(FlyingHelperKind.None);
            }

            if (equippedKind == FlyingHelperKind.None || _kind == FlyingHelperKind.None)
                return;

            _spawnInitialized = false;
            _legacyAccumulator = 0f;
            _darkRavenMotion = DarkRavenMotion.Flying;
            ClearDarkRavenAttack();
            Hidden = true;
            Alpha = 0f;
            BlendMeshLight = 1f;
            ClearSparks();
            InvalidateBuffers(BufferFlagLighting | BufferFlagMaterial | BufferFlagAnimation);
        }

        private static float GetWorldScale(FlyingHelperKind kind) => kind switch
        {
            FlyingHelperKind.GuardianAngel => GuardianWorldScale,
            FlyingHelperKind.Imp => ImpWorldScale,
            FlyingHelperKind.DarkRaven => DarkRavenWorldScale,
            _ => ImpWorldScale
        };

        private static float GetCharacterSceneScale(FlyingHelperKind kind) => kind switch
        {
            FlyingHelperKind.GuardianAngel => GuardianCharacterSceneScale,
            FlyingHelperKind.Imp => ImpCharacterSceneScale,
            FlyingHelperKind.DarkRaven => DarkRavenCharacterSceneScale,
            _ => ImpCharacterSceneScale
        };

        private void ConfigureWingAnimation()
        {
            if (_kind == FlyingHelperKind.DarkRaven)
            {
                CurrentAction = ResolveWingAnimationAction();
                AnimationSpeed = DarkRavenAnimationSpeed;
            }
            else
            {
                CurrentAction = ResolveWingAnimationAction();
                AnimationSpeed = HelperWingAnimationSpeed;
            }

            ContinuousAnimation = true;
            FreezeAnimationPose = false;
            InvalidateBuffers(BufferFlagAnimation);
        }

        private int ResolveWingAnimationAction()
        {
            if (Model?.Actions == null || Model.Actions.Length == 0)
                return 0;

            if (Model.Actions[0] != null && Model.Actions[0].NumAnimationKeys > 1)
                return 0;

            for (int i = 1; i < Model.Actions.Length; i++)
            {
                if (Model.Actions[i] != null && Model.Actions[i].NumAnimationKeys > 1)
                    return i;
            }

            return 0;
        }

        private void UpdateImpShoulderTransform(PlayerObject owner)
        {
            Vector3 shoulder = GetImpShoulderPosition(owner);
            _previousSimulationPosition = shoulder;
            _simulationPosition = shoulder;
            _simulationYaw = owner.Angle.Z;
            Position = shoulder;
            Angle = new Vector3(0f, 0f, owner.Angle.Z);
        }

        private Vector3 GetImpShoulderPosition(PlayerObject owner)
        {
            // Original client (RenderLinkObject for MODEL_IMP): the imp is linked to player bone 34
            // with a local offset of (20, 0, 0).
            if (owner.TryGetBoneWorldMatrix(ImpLinkBoneIndex, out Matrix boneMatrix))
                return Vector3.Transform(ImpLinkOffset, boneMatrix);

            float shoulderHeight = owner.BodyHeight > 1f ? owner.BodyHeight * 0.72f : 110f;
            return owner.WorldPosition.Translation + new Vector3(0f, 0f, shoulderHeight);
        }

        private int FindSparkSlot()
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                if (!_sparks[i].Active)
                    return i;
            }

            return _random.Next(_sparks.Length);
        }

        private void SpawnSpark()
        {
            int slot = FindSparkSlot();

            Vector3 position = _kind == FlyingHelperKind.DarkRaven
                ? GetDarkRavenSparkPosition()
                : _simulationPosition + new Vector3(
                    _random.Next(-8, 8),
                    _random.Next(-8, 8),
                    _random.Next(-8, 8));

            int lifeTicks = 10 + _random.Next(0, 5);
            _sparksUsed = true;
            _sparks[slot] = new SparkParticle
            {
                Active = true,
                UseLightTexture = false,
                Color = Vector3.Zero,
                Position = position,
                PreviousPosition = position,
                VelocityPerTick = new Vector3(
                    (_random.NextSingle() - 0.5f) * 1.2f,
                    (_random.NextSingle() - 0.5f) * 1.2f,
                    0.7f + _random.NextSingle() * 1.1f),
                Rotation = _random.NextSingle() * MathHelper.TwoPi,
                RotationPerTick = (_random.NextSingle() - 0.5f) * 0.35f,
                Scale = 0.16f + _random.NextSingle() * 0.10f,
                LifeTicks = lifeTicks,
                MaxLifeTicks = lifeTicks
            };
        }

        private Vector3 GetDarkRavenSparkPosition()
        {
            Matrix[] bones = GetBoneTransforms();
            if (bones != null && bones.Length > 0)
            {
                int boneCount = Math.Min(DarkRavenSparkBoneCount, bones.Length);
                int boneIndex = _random.Next(boneCount);
                return Vector3.Transform(
                    Vector3.Zero,
                    bones[boneIndex] * GetDarkRavenSimulationWorldMatrix());
            }

            return _simulationPosition;
        }

        private Vector3 GetDarkRavenBoneWorldPosition(
            int boneIndex,
            Vector3 localPosition,
            Vector3 fallback)
        {
            Matrix[] bones = GetBoneTransforms();
            if (bones != null && (uint)boneIndex < (uint)bones.Length)
            {
                return Vector3.Transform(
                    localPosition,
                    bones[boneIndex] * GetDarkRavenSimulationWorldMatrix());
            }

            return fallback;
        }

        private Matrix GetDarkRavenSimulationWorldMatrix()
        {
            return Matrix.CreateScale(Scale) *
                   Matrix.CreateFromQuaternion(
                       Client.Main.Core.Utilities.MathUtils.AngleQuaternion(
                           new Vector3(0f, 0f, _simulationYaw))) *
                   Matrix.CreateTranslation(_simulationPosition);
        }

        private void SpawnDarkRavenAttackLight(Vector3 position, Vector3 color, float scale)
        {
            int slot = FindSparkSlot();
            int lifeTicks = 4 + _random.Next(0, 4);
            _sparksUsed = true;
            _sparks[slot] = new SparkParticle
            {
                Active = true,
                UseLightTexture = true,
                Color = color,
                Position = position,
                PreviousPosition = position,
                VelocityPerTick = new Vector3(
                    (_random.NextSingle() - 0.5f) * 2.4f,
                    (_random.NextSingle() - 0.5f) * 2.4f,
                    (_random.NextSingle() - 0.5f) * 1.8f),
                Rotation = _random.NextSingle() * MathHelper.TwoPi,
                RotationPerTick = (_random.NextSingle() - 0.5f) * 0.5f,
                Scale = scale,
                LifeTicks = lifeTicks,
                MaxLifeTicks = lifeTicks
            };
        }

        private void SpawnDarkRavenMagicAttackLights()
        {
            Matrix[] bones = GetBoneTransforms();
            if (bones == null || bones.Length == 0)
                return;

            int boneCount = Math.Min(DarkRavenAttackBoneCount, bones.Length);
            int start = _random.Next(2);
            for (int i = start; i < boneCount; i += 2)
            {
                if (Model?.Bones != null && Model.Bones[i] == BMDTextureBone.Dummy)
                    continue;

                Vector3 position = Vector3.Transform(Vector3.Zero, bones[i] * WorldPosition);
                SpawnDarkRavenAttackLight(position, new Vector3(1f, 0.6f, 0.4f), 0.11f);
            }
        }

        private void SpawnDarkRavenMagicImpact(Vector3 position)
        {
            for (int i = 0; i < 12; i++)
            {
                SpawnDarkRavenAttackLight(
                    position + new Vector3(
                        _random.Next(-24, 25),
                        _random.Next(-24, 25),
                        _random.Next(0, 42)),
                    new Vector3(1f, 0.6f, 0.4f),
                    0.16f);
            }
        }

        private void SpawnDarkRavenModelEffect(
            string modelPath,
            string fallbackModelPath,
            Vector3 position,
            Vector3 angle,
            Vector3 light,
            float scale,
            int blendMesh,
            float lifeFrames,
            float growthPerFrame = 0f,
            float growthAcceleration = 0f,
            float fadeBase = 1f,
            float fadeStartFrame = 0f)
        {
            if (World == null)
                return;

            World.Objects.Add(new DarkRavenAttackModelEffect(
                modelPath,
                fallbackModelPath,
                position,
                angle,
                light,
                scale,
                blendMesh,
                lifeFrames,
                growthPerFrame,
                growthAcceleration,
                fadeBase,
                fadeStartFrame,
                modelPath.EndsWith("DarkLordSkill.bmd", StringComparison.OrdinalIgnoreCase)
                    ? DarkRavenSkillSpinRadiansPerFrame
                    : 0f));
        }

        private void UpdateSparks()
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                if (!_sparks[i].Active)
                    continue;

                SparkParticle spark = _sparks[i];
                spark.PreviousPosition = spark.Position;
                spark.Position += spark.VelocityPerTick;
                spark.VelocityPerTick *= 0.92f;
                spark.Rotation += spark.RotationPerTick;
                spark.LifeTicks--;
                if (spark.LifeTicks <= 0)
                    spark.Active = false;
                _sparks[i] = spark;
            }
        }

        private void DrawGuardianGlow(float alpha)
        {
            if (_lightTexture == null || _lightTexture.IsDisposed)
                return;

            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;
            float luminosity = 0.70f + _random.NextSingle() * 0.30f;
            Vector3 light = new(
                luminosity * 0.5f,
                luminosity * 0.8f,
                luminosity * 0.6f);
            Color color = ToAdditiveColor(light * alpha);
            float halfSize = 32f * (Scale / 1f);

            WriteBillboardQuad(0, Position, cameraRight * halfSize, cameraUp * halfSize, color);
            DrawBillboardBatch(_lightTexture, 1);
        }

        private void DrawGuardianSparks(float alpha)
        {
            DrawSparks(alpha, new Vector3(0.4f, 0.4f, 0.4f), 1f);
        }

        private void DrawDarkRavenSparks(float alpha)
        {
            DrawSparks(alpha, new Vector3(0.22f, 0.45f, 1f), 0.9f);
            DrawDarkRavenAttackLights(alpha);
        }

        private void DrawSparks(float alpha, Vector3 sparkColor, float sizeMultiplier)
        {
            if (_sparkTexture == null || _sparkTexture.IsDisposed)
                return;

            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;
            float interpolation = MathHelper.Clamp(_legacyAccumulator / LegacyStepSeconds, 0f, 1f);
            int quadCount = 0;

            for (int i = 0; i < _sparks.Length && quadCount < MaxSparks; i++)
            {
                SparkParticle spark = _sparks[i];
                if (!spark.Active || spark.UseLightTexture)
                    continue;

                Vector3 center = Vector3.Lerp(spark.PreviousPosition, spark.Position, interpolation);
                float life = MathHelper.Clamp(spark.LifeTicks / (float)Math.Max(1, spark.MaxLifeTicks), 0f, 1f);
                float fadeIn = MathHelper.Clamp((1f - life) * 5f, 0f, 1f);
                float intensity = MathF.Pow(life, 0.8f) * fadeIn * alpha;
                float halfSize = 9f * spark.Scale * (0.65f + life * 0.35f) * sizeMultiplier * (Scale / 1f);

                Vector3 right = cameraRight * halfSize;
                Vector3 up = cameraUp * halfSize;
                RotateBillboardAxes(ref right, ref up, spark.Rotation);
                Color color = ToAdditiveColor(sparkColor * intensity);
                WriteBillboardQuad(quadCount++, center, right, up, color);
            }

            DrawBillboardBatch(_sparkTexture, quadCount);
        }

        private void DrawDarkRavenAttackLights(float alpha)
        {
            if (_lightTexture == null || _lightTexture.IsDisposed)
                return;

            Vector3 cameraRight = Camera.Instance.Right;
            Vector3 cameraUp = Camera.Instance.Up;
            float interpolation = MathHelper.Clamp(_legacyAccumulator / LegacyStepSeconds, 0f, 1f);
            int quadCount = 0;

            for (int i = 0; i < _sparks.Length && quadCount < MaxSparks; i++)
            {
                SparkParticle spark = _sparks[i];
                if (!spark.Active || !spark.UseLightTexture)
                    continue;

                Vector3 center = Vector3.Lerp(spark.PreviousPosition, spark.Position, interpolation);
                float life = MathHelper.Clamp(spark.LifeTicks / (float)Math.Max(1, spark.MaxLifeTicks), 0f, 1f);
                float intensity = MathF.Pow(life, 0.7f) * alpha;
                float halfSize = 14f * spark.Scale * (0.75f + life * 0.35f) * (Scale / 1f);
                Vector3 right = cameraRight * halfSize;
                Vector3 up = cameraUp * halfSize;
                RotateBillboardAxes(ref right, ref up, spark.Rotation);
                WriteBillboardQuad(quadCount++, center, right, up,
                    ToAdditiveColor(spark.Color * intensity));
            }

            DrawBillboardBatch(_lightTexture, quadCount);
        }

        private void DrawBillboardBatch(Texture2D texture, int quadCount)
        {
            if (quadCount <= 0 || _billboardEffect == null)
                return;

            _billboardEffect.Texture = texture;
            _billboardEffect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _billboardVertices,
                0,
                quadCount * 4,
                BillboardIndices,
                0,
                quadCount * 2);
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
                center - right - up, color, new Vector2(0f, 1f));
            _billboardVertices[vertex + 1] = new VertexPositionColorTexture(
                center + right - up, color, new Vector2(1f, 1f));
            _billboardVertices[vertex + 2] = new VertexPositionColorTexture(
                center + right + up, color, new Vector2(1f, 0f));
            _billboardVertices[vertex + 3] = new VertexPositionColorTexture(
                center - right + up, color, new Vector2(0f, 0f));
        }

        private void ClearSparks()
        {
            // Called every simulation step for helpers that never emit sparks (Imp, safe-zone raven).
            if (!_sparksUsed)
                return;
            Array.Clear(_sparks, 0, _sparks.Length);
            _sparksUsed = false;
        }

        private void ClearDarkRavenAttack()
        {
            _darkRavenAttackKind = null;
            _darkRavenAttackTargetId = NoDarkRavenTarget;
            _darkRavenAttackTime = 0;
            _darkRavenAttackFlashSpawned = false;
        }

        private static float TurnTowards(float current, float target, float maxStep)
        {
            float delta = MathHelper.WrapAngle(target - current);
            return MathHelper.WrapAngle(current + MathHelper.Clamp(delta, -maxStep, maxStep));
        }

        private static void RotateBillboardAxes(ref Vector3 right, ref Vector3 up, float radians)
        {
            float cosine = MathF.Cos(radians);
            float sine = MathF.Sin(radians);
            Vector3 originalRight = right;
            right = originalRight * cosine + up * sine;
            up = up * cosine - originalRight * sine;
        }

        private static Color ToAdditiveColor(Vector3 color)
        {
            return new Color(
                MathHelper.Clamp(color.X, 0f, 1f),
                MathHelper.Clamp(color.Y, 0f, 1f),
                MathHelper.Clamp(color.Z, 0f, 1f),
                1f);
        }

        private async Task<BMD?> PrepareModelWithFallbackAsync(
            string primaryPath,
            FlyingHelperKind kind)
        {
            string fileName = kind switch
            {
                FlyingHelperKind.GuardianAngel => "helper01.bmd",
                FlyingHelperKind.Imp => "helper02.bmd",
                FlyingHelperKind.DarkRaven => "DarkSpirit.bmd",
                _ => string.Empty
            };

            string? inventoryPath = null;
            if (kind != FlyingHelperKind.DarkRaven)
            {
                short itemNumber = kind == FlyingHelperKind.GuardianAngel ? (short)0 : (short)1;
                var definition = ItemDatabase.GetItemDefinition(13, itemNumber);
                inventoryPath = NormalizeModelPath(definition?.TexturePath);
            }

            // Use exactly the model selected by the item database first for the legacy helper
            // models. Dark Raven is a skill model in SourceMain, so it intentionally bypasses the
            // inventory item model (SpiritBill) and loads Skill/DarkSpirit.bmd instead.
            string?[] paths = kind == FlyingHelperKind.DarkRaven
                ? new[] { primaryPath, "Skill/darkspirit.bmd" }
                : new[] { inventoryPath, primaryPath, $"Item/{fileName}" };

            for (int i = 0; i < paths.Length; i++)
            {
                string? path = paths[i];
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                bool duplicate = false;
                for (int previous = 0; previous < i; previous++)
                {
                    if (string.Equals(paths[previous], path, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                    continue;

                var model = await BMDLoader.Instance.Prepare(path).ConfigureAwait(false);
                if (model != null)
                    return model;
            }

            return null;
        }

        private static string? NormalizeModelPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = path.Replace('\\', '/').TrimStart('/');
            if (normalized.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[5..];

            return normalized;
        }

        private static async Task<Texture2D?> PrepareTextureAsync(string path)
        {
            try
            {
                await TextureLoader.Instance.Prepare(path);
                return await TextureLoader.Instance.PrepareAndGetTexture(path);
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D CreateRadialTexture(GraphicsDevice graphicsDevice, int size, float exponent)
        {
            var texture = new Texture2D(graphicsDevice, size, size);
            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    float intensity = MathF.Pow(MathHelper.Clamp(1f - distance, 0f, 1f), exponent);
                    pixels[y * size + x] = new Color(intensity, intensity, intensity, intensity);
                }
            }

            texture.SetData(pixels);
            return texture;
        }

        private static bool IsChaosCastleMap(short worldIndex) =>
            (worldIndex >= 18 && worldIndex <= 23) || worldIndex == 53 || worldIndex == 97;

        public override void Dispose()
        {
            _billboardEffect?.Dispose();
            _billboardEffect = null;

            if (_ownsSparkTexture)
                _sparkTexture?.Dispose();
            if (_ownsLightTexture)
                _lightTexture?.Dispose();

            _sparkTexture = null;
            _lightTexture = null;
            base.Dispose();
        }

        private struct SparkParticle
        {
            public bool Active;
            public bool UseLightTexture;
            public Vector3 Color;
            public Vector3 Position;
            public Vector3 PreviousPosition;
            public Vector3 VelocityPerTick;
            public float Rotation;
            public float RotationPerTick;
            public float Scale;
            public int LifeTicks;
            public int MaxLifeTicks;
        }

        private enum DarkRavenMotion
        {
            Flying,
            Landing,
            Standing
        }
    }
}
