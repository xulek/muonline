using Client.Main.Controllers;              // GraphicsManager
using Client.Main.Core.Models;              // ScopeObject
using Client.Main.Graphics;
using Client.Main.Models;                   // MessageType
using Client.Main.Networking.Services;      // CharacterService
using Client.Main.Controls.UI;              // ChatLogWindow + LabelControl
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Helpers;
using Client.Main.Content;
using Client.Main.Scenes;
using Client.Main.Objects.Effects;

namespace Client.Main.Objects
{
    /// <summary>
    /// Dropped item or Zen; the label disappears only when the server
    /// removes the object from scope.
    /// </summary>
    public class DroppedItemObject : WorldObject
    {
        // ─────────────────── constants
        private const float TerrainPenetrationClearance = 2f;
        private const float PickupRange = 300f;
        private const float LabelOffsetZ = 10f;
        private const int LabelPixelGap = 20;
        private const float BoundingPadding = 2f; // Small padding for interaction
        private const int MaxZenCoins = 80;
        private const float FreshDropStartHeight = 180f;
        private const float FreshDropInitialGravity = 20f;
        private const float FreshDropGravityStep = 6f;
        private const float LabelVisibilityDistSq = 2000f * 2000f; // Squared distance for label visibility check

        internal int TileKey => HashCode.Combine(_scope.PositionX, _scope.PositionY);
        internal bool RenderVisuals { get; set; } = true;

        // ─────────────────── deps / state
        private ScopeObject _scope;
        private ushort _mainPlayerId;
        private CharacterService _charSvc;
        private ILogger<DroppedItemObject> _log;

        private SpriteFont _font;
        private bool _pickedUp;
        private ModelObject _modelObj; // Optional 3D model when available
        private ItemDefinition _definition;
        private ItemDatabase.ItemDetails _itemDetails;
        private bool _isMoney;
        private bool _isFreshDrop;
        private Color _labelColor;
        private readonly List<ModelObject> _coinModels = new List<ModelObject>(); // Multiple coins for money piles
        private readonly DroppedItemVisual _visual = new();
        private int _loadGeneration;
        private bool _visualContentReady;
        private bool _terrainPlacementReady;
        private bool _freshDropMotionInitialized;
        private bool _freshDropInFlight;
        private float _freshDropGroundZ;
        private float _freshDropGravity;

        // ─────────────────── public helpers
        public ushort RawId => _scope?.RawId ?? 0;
        internal int LoadGeneration => _loadGeneration;
        public new string DisplayName { get; private set; }
        internal Vector3 ShineAngle => _modelObj?.Angle
            ?? (_coinModels.Count > 0 ? _coinModels[0].Angle : Vector3.Zero);

        internal void PrepareRenderResourcesForFirstFrame()
        {
            _modelObj?.PrepareRenderResourcesForFirstFrame();
            for (int i = 0; i < _coinModels.Count; i++)
                _coinModels[i]?.PrepareRenderResourcesForFirstFrame();
        }

        internal async Task PrepareGpuTexturesForFirstFrameAsync()
        {
            if (_modelObj != null)
                await _modelObj.PrepareGpuTexturesForFirstFrameAsync().ConfigureAwait(false);

            for (int i = 0; i < _coinModels.Count; i++)
            {
                ModelObject coin = _coinModels[i];
                if (coin != null)
                    await coin.PrepareGpuTexturesForFirstFrameAsync().ConfigureAwait(false);
            }
        }

        // Pool
        private static readonly System.Collections.Concurrent.ConcurrentBag<DroppedItemObject> _pool = new();

        public static DroppedItemObject Rent(
              ScopeObject scope,
              ushort mainPlayerId,
              CharacterService charSvc,
              ILogger<DroppedItemObject> logger = null,
              bool isFreshDrop = false)
        {
            if (_pool.TryTake(out var obj))
            {
                obj.ResetFromScope(scope, mainPlayerId, charSvc, logger, isFreshDrop);
                return obj;
            }
            return new DroppedItemObject(scope, mainPlayerId, charSvc, logger, isFreshDrop);
        }

        public void Recycle()
        {
            _loadGeneration++;

            // Never return an object to the pool while an old asynchronous Load() can still
            // write to it. Such an object is simply left for normal GC after its load exits.
            bool canReturnToPool = !IsLoadInProgress;
            Dispose();

            if (canReturnToPool && !IsLoadInProgress)
                _pool.Add(this);
        }

        // =====================================================================
        public DroppedItemObject(
              ScopeObject scope,
              ushort mainPlayerId,
              CharacterService charSvc,
              ILogger<DroppedItemObject> logger = null,
              bool isFreshDrop = false)
        {
            ResetFromScope(scope, mainPlayerId, charSvc, logger, isFreshDrop);
        }

        private void ResetFromScope(
            ScopeObject scope,
            ushort mainPlayerId,
            CharacterService charSvc,
            ILogger<DroppedItemObject> logger,
            bool isFreshDrop)
        {
            if (!TryResetLifecycleForReuse())
                throw new InvalidOperationException("A dropped item was reused while its previous load was still running.");

            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
            _loadGeneration++;
            _mainPlayerId = mainPlayerId;
            _charSvc = charSvc ?? throw new ArgumentNullException(nameof(charSvc));
            _log = logger ?? ModelObject.AppLoggerFactory?.CreateLogger<DroppedItemObject>() ?? NullLogger<DroppedItemObject>.Instance;

            NetworkId = scope.Id;
            Interactive = true;
            Hidden = false;
            Status = GameControlStatus.NonInitialized;
            _pickedUp = false;
            _modelObj = null;
            _definition = null;
            _isMoney = false;
            _isFreshDrop = isFreshDrop;
            _coinModels.Clear();
            _visual.Reset();
            _visualContentReady = false;
            _terrainPlacementReady = false;
            _freshDropMotionInitialized = false;
            _freshDropInFlight = false;
            _freshDropGroundZ = 0f;
            _freshDropGravity = 0f;
            RenderVisuals = true;

            // Initialize position at ground level (will be adjusted in Load() after terrain height is known)
            Position = new(
                scope.PositionX * Constants.TERRAIN_SCALE + Constants.TERRAIN_SCALE / 2f,
                scope.PositionY * Constants.TERRAIN_SCALE + Constants.TERRAIN_SCALE / 2f,
                0f); // Ground level, bottom of bounding box

            string baseName = "Unknown Drop";
            ItemDatabase.ItemDetails itemDetails = default;

            if (scope is ItemScopeObject itemScope)
            {
                ReadOnlySpan<byte> itemData = itemScope.ItemData.Span;
                baseName = itemScope.ItemDescription;
                itemDetails = ItemDatabase.ParseItemDetails(itemData);
                _definition = ItemDatabase.GetItemDefinition(itemData);
                _isMoney = false;
            }
            else if (scope is MoneyScopeObject moneyScope)
            {
                baseName = $"{moneyScope.Amount} Zen";
                _isMoney = true;
            }

            _itemDetails = itemDetails;
            DisplayName = FormatItemDisplayName(baseName, itemDetails);
            _labelColor = GetLabelColor(scope, itemDetails);
        }

        // =====================================================================
public override async Task Load()
{
var loadGeneration = _loadGeneration;
await base.Load();
var world = World;
if (Status != GameControlStatus.Ready || !CanContinueLoad(world, loadGeneration))
return;

            // Terrain can still be loading when scope objects begin initialization. Do not
            // permanently lock the drop to Z=0; placement is retried from Update until both
            // terrain and the visual model are ready.
            TryFinalizeTerrainPlacement(world);

            _font = GraphicsManager.Instance.Font;

            // Handle money (gold coin) model - create a pile of coins
            if (_isMoney)
            {
                try
                {
var bmd = await BMDLoader.Instance.Prepare("Item\\Gold01.bmd");
if (!CanContinueLoad(world, loadGeneration))
return;
                    if (bmd == null)
                    {
                        _log.LogWarning("Gold coin BMD model is null after loading");
                        return;
                    }

                    // SourceMain5.2 RenderZen: clamp(sqrt(amount) / 2, 3, 80).
                    var moneyScope = _scope as MoneyScopeObject;
                    int coinCount = CalculateCoinCount(moneyScope?.Amount ?? 0);
                    var model = new DroppedZenModel();
                    model.Model = bmd;
                    model.Angle = new Vector3(0f, 0f, MathHelper.ToRadians(-45f));
                    model.Scale = 0.8f;
                    model.LightEnabled = true;
                    model.ConfigureCoinLayout(coinCount, RawId);

                    if (!await TryLoadChildModel(model, world, loadGeneration))
                        return;
                    _coinModels.Add(model);

                    RecenterCoinsAndFitBoundingBox();
                    _visualContentReady = true;
                    TryFinalizeTerrainPlacement(world);
                    _log.LogDebug("Gold coin pile loaded with {Count} coins at position {Pos}", coinCount, Position);
                    AttachShineEffect();
                    return; // 3D model loaded
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to load gold coin BMD model");
                }
            }
            // Handle item models
            else if (_definition != null && !string.IsNullOrEmpty(_definition.TexturePath))
            {
                // Try to load real 3D model
                if (_definition.TexturePath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
var bmd = await BMDLoader.Instance.Prepare(_definition.TexturePath);
if (!CanContinueLoad(world, loadGeneration))
return;
var model = new DroppedItemModel();
                        model.Model = bmd;
                        model.ItemDefinition = _definition;

                        model.Position = Vector3.Zero;

                        // Use original rotation from ItemOrientationHelper
                        var baseAngle = ItemOrientationHelper.GetWorldDropEuler(_definition);
                        model.Angle = new Vector3(
                            baseAngle.X + MathHelper.PiOver2,
                            baseAngle.Y - MathHelper.PiOver2,
                            baseAngle.Z + MathHelper.PiOver2 / 2f
                        );

                        model.Scale = 0.6f;
                        model.LightEnabled = true;

                        // SourceMain5.2 RenderItem draws drops through RenderPartObject, so they
                        // show the same upgrade, excellent and ancient effects as equipped items.
                        model.MaterialItemGroup = _definition.Group;
                        model.MaterialItemIndex = _definition.Id;
                        model.ItemLevel = _itemDetails.Level;
                        model.IsExcellentItem = _itemDetails.IsExcellent;
                        model.IsAncientItem = _itemDetails.IsAncient;

if (!await TryLoadChildModel(model, world, loadGeneration))
return;
_modelObj = model;

                        // Position model so its bottom touches the parent ground plane, then
                        // lift the complete rotated model only when a terrain triangle penetrates it.
                        PositionModelOnGround(model);
                        _visualContentReady = true;
                        TryFinalizeTerrainPlacement(world);

                        AttachShineEffect();
                        return; // 3D model loaded
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "Failed to load BMD model for dropped item: {Path}", _definition.TexturePath);
                    }
                }
            }

            _visualContentReady = true;
            TryFinalizeTerrainPlacement(world);
            AttachShineEffect();
        }

        // =====================================================================
        private bool CanContinueLoad(Client.Main.Controls.WorldControl expectedWorld, int loadGeneration)
        {
            return expectedWorld != null
                && ReferenceEquals(World, expectedWorld)
                && _loadGeneration == loadGeneration
                && Status != GameControlStatus.Disposed;
        }

        private async Task<bool> TryLoadChildModel(ModelObject model, Client.Main.Controls.WorldControl expectedWorld, int loadGeneration)
        {
            if (model == null || !CanContinueLoad(expectedWorld, loadGeneration))
                return false;

            Children.Add(model);

            if (!CanContinueLoad(expectedWorld, loadGeneration))
                return false;

            await model.Load();
            return model.Status == GameControlStatus.Ready && CanContinueLoad(expectedWorld, loadGeneration);
        }

        /// <summary>
        /// Places the parent drop on the current terrain and then applies a geometry-aware lift.
        /// The operation is retried while TerrainControl is still loading, so a temporary height
        /// value of zero can never become the permanent world position of the item.
        /// </summary>
        private bool TryFinalizeTerrainPlacement(Client.Main.Controls.WorldControl expectedWorld)
        {
            if (!_visualContentReady ||
                expectedWorld == null ||
                !ReferenceEquals(World, expectedWorld) ||
                expectedWorld.Terrain == null ||
                expectedWorld.Terrain.Status != GameControlStatus.Ready)
            {
                _terrainPlacementReady = false;
                return false;
            }

            float groundZ = expectedWorld.Terrain.RequestTerrainHeight(Position.X, Position.Y);
            if (float.IsNaN(groundZ) || float.IsInfinity(groundZ))
            {
                _terrainPlacementReady = false;
                return false;
            }

            // Always restart from the terrain height at the drop center. LiftVisualsAboveTerrain
            // then accounts for slopes under every transformed vertex of the item model.
            Position = new Vector3(Position.X, Position.Y, groundZ);
            LiftVisualsAboveTerrain();
            _freshDropGroundZ = Position.Z;

            if (_isFreshDrop && !_freshDropMotionInitialized)
            {
                _freshDropMotionInitialized = true;
                _freshDropInFlight = true;
                _freshDropGravity = FreshDropInitialGravity;
                Position = new Vector3(Position.X, Position.Y, _freshDropGroundZ + FreshDropStartHeight);
            }

            _terrainPlacementReady = true;
            return true;
        }

        /// <summary>
        /// Positions the model so its lowest vertex touches the ground (parent's Z=0 in local space).
        /// Calculates bounding box directly from model geometry.
        /// </summary>
        private void PositionModelOnGround(ModelObject model)
        {
            if (model?.Model?.Meshes == null)
            {
                // Fallback bounding box
                BoundingBoxLocal = new BoundingBox(
                    new Vector3(-20, -20, 0),
                    new Vector3(20, 20, 40));
                return;
            }

            var bmd = model.Model;
            var bones = model.GetBoneTransforms();

            // Find model bounds directly from vertices
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            bool hasVertices = false;

            // Build rotation matrix from model angle
            Matrix rotationMatrix = Matrix.CreateRotationX(model.Angle.X) *
                                   Matrix.CreateRotationY(model.Angle.Y) *
                                   Matrix.CreateRotationZ(model.Angle.Z);

            foreach (var mesh in bmd.Meshes)
            {
                if (mesh.Vertices == null) continue;

                foreach (var vert in mesh.Vertices)
                {
                    // Transform vertex by bone
                    Matrix boneMatrix = Matrix.Identity;
                    if (bones != null && vert.Node >= 0 && vert.Node < bones.Length)
                    {
                        boneMatrix = bones[vert.Node];
                    }

                    // Vertex position in model's local space
                    Vector3 localPos = new Vector3(vert.Position.X, vert.Position.Y, vert.Position.Z);
                    Vector3 transformedPos = Vector3.Transform(localPos, boneMatrix);

                    // Apply model rotation
                    Vector3 rotatedPos = Vector3.Transform(transformedPos, rotationMatrix);

                    // Apply scale
                    rotatedPos *= model.Scale;

                    min = Vector3.Min(min, rotatedPos);
                    max = Vector3.Max(max, rotatedPos);
                    hasVertices = true;
                }
            }

            if (!hasVertices)
            {
                // Fallback bounding box
                BoundingBoxLocal = new BoundingBox(
                    new Vector3(-20, -20, 0),
                    new Vector3(20, 20, 40));
                return;
            }

            // Move model up so its lowest point is at Z=0 (ground level)
            float groundOffset = -min.Z;
            model.Position = new Vector3(
                -(min.X + max.X) * 0.5f,  // Center X
                -(min.Y + max.Y) * 0.5f,  // Center Y
                groundOffset               // Lift to ground level
            );

            // Calculate bounds after repositioning
            float halfWidth = MathF.Max((max.X - min.X) * 0.5f, 10f);
            float halfDepth = MathF.Max((max.Y - min.Y) * 0.5f, 10f);
            float height = MathF.Max(max.Z - min.Z, 15f);

            // Set bounding box with minimal padding
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-halfWidth - BoundingPadding, -halfDepth - BoundingPadding, 0f),
                new Vector3(halfWidth + BoundingPadding, halfDepth + BoundingPadding, height + BoundingPadding));
        }

        // =====================================================================
        /// <summary>
        /// Lifts the parent drop only by the amount required to keep every rendered
        /// model vertex above the actual terrain triangles. Existing placement is
        /// preserved when there is no penetration.
        /// </summary>
        private void LiftVisualsAboveTerrain()
        {
            if (World?.Terrain == null)
                return;

            float requiredLift = 0f;

            if (_modelObj != null)
                requiredLift = MathF.Max(requiredLift, CalculateRequiredTerrainLift(_modelObj));

            for (int i = 0; i < _coinModels.Count; i++)
                requiredLift = MathF.Max(requiredLift, CalculateRequiredTerrainLift(_coinModels[i]));

            if (requiredLift <= 0.001f || float.IsNaN(requiredLift) || float.IsInfinity(requiredLift))
                return;

            Position = new Vector3(Position.X, Position.Y, Position.Z + requiredLift);
        }

        /// <summary>
        /// Calculates how far a child model must be lifted so none of its transformed
        /// vertices are below the terrain surface. This runs once when the drop loads.
        /// </summary>
        private float CalculateRequiredTerrainLift(ModelObject model)
        {
            if (model?.Model?.Meshes == null || World?.Terrain == null)
                return 0f;

            var bones = model.GetBoneTransforms();
            Matrix rotationMatrix = Matrix.CreateRotationX(model.Angle.X) *
                                    Matrix.CreateRotationY(model.Angle.Y) *
                                    Matrix.CreateRotationZ(model.Angle.Z);

            float requiredLift = 0f;

            foreach (var mesh in model.Model.Meshes)
            {
                if (mesh?.Vertices == null)
                    continue;

                foreach (var vert in mesh.Vertices)
                {
                    Matrix boneMatrix = Matrix.Identity;
                    if (bones != null && vert.Node >= 0 && vert.Node < bones.Length)
                        boneMatrix = bones[vert.Node];

                    Vector3 localPos = new Vector3(vert.Position.X, vert.Position.Y, vert.Position.Z);
                    Vector3 transformedPos = Vector3.Transform(localPos, boneMatrix);
                    transformedPos = Vector3.Transform(transformedPos, rotationMatrix) * model.Scale;
                    transformedPos += model.Position;

                    float worldX = Position.X + transformedPos.X;
                    float worldY = Position.Y + transformedPos.Y;
                    float vertexWorldZ = Position.Z + transformedPos.Z;
                    float terrainZ = World.Terrain.RequestTerrainHeight(worldX, worldY);

                    float penetration = terrainZ + TerrainPenetrationClearance - vertexWorldZ;
                    if (penetration > requiredLift)
                        requiredLift = penetration;
                }
            }

            return requiredLift;
        }

        // =====================================================================
        /// <summary>
        /// Centers coin pile and fits bounding box for money drops.
        /// </summary>
        private void RecenterCoinsAndFitBoundingBox()
        {
            if (_coinModels.Count == 0)
                return;

            if (_coinModels.Count == 1 && _coinModels[0] is DroppedZenModel zenModel)
            {
                BoundingBoxLocal = zenModel.PileBounds;
                return;
            }

            // Calculate bounds from coin positions
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            foreach (var coin in _coinModels)
            {
                Vector3 coinPos = coin.Position;
                float coinRadius = 12f * coin.Scale;
                float coinHeight = 4f * coin.Scale;

                min = Vector3.Min(min, coinPos - new Vector3(coinRadius, coinRadius, 0));
                max = Vector3.Max(max, coinPos + new Vector3(coinRadius, coinRadius, coinHeight));
            }

            // Center in X/Y, keep Z at ground level
            float centerX = (min.X + max.X) * 0.5f;
            float centerY = (min.Y + max.Y) * 0.5f;
            float minZ = MathF.Min(min.Z, 0f);

            foreach (var coin in _coinModels)
            {
                coin.Position = new Vector3(
                    coin.Position.X - centerX,
                    coin.Position.Y - centerY,
                    coin.Position.Z - minZ
                );
            }

            // Recalculate bounds after centering
            float halfWidth = MathF.Max((max.X - min.X) * 0.5f, 15f);
            float halfDepth = MathF.Max((max.Y - min.Y) * 0.5f, 15f);
            float height = MathF.Max(max.Z - min.Z, 10f);

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-halfWidth - BoundingPadding, -halfDepth - BoundingPadding, 0f),
                new Vector3(halfWidth + BoundingPadding, halfDepth + BoundingPadding, height + BoundingPadding));
        }

        // =====================================================================
        public override void Update(GameTime gameTime)
        {
            if (!_terrainPlacementReady && _visualContentReady && World != null)
                TryFinalizeTerrainPlacement(World);

            if (_terrainPlacementReady && _freshDropInFlight)
            {
                float factor = MathF.Max(0.01f, FPSCounter.Instance.FPS_ANIMATION_FACTOR);
                Position = new Vector3(
                    Position.X,
                    Position.Y,
                    Position.Z + _freshDropGravity * factor);
                _freshDropGravity -= FreshDropGravityStep * factor;

                if (Position.Z <= _freshDropGroundZ)
                {
                    Position = new Vector3(Position.X, Position.Y, _freshDropGroundZ);
                    _freshDropInFlight = false;
                }
            }

            // SourceMain5.2 moves the drop before calling CreateShiny. Updating the
            // vertical motion first keeps the particle origin on the current frame.
            base.Update(gameTime);
        }

        // =====================================================================
        public override void Draw(GameTime gameTime)
        {
            if (!Visible || !_terrainPlacementReady) return;

            DrawBoundingBox3D();

            if (!RenderVisuals)
                return;

            var objects = Children;
            for (int i = 0; i < objects.Count; i++)
            {
                var child = objects[i];
                if (_visual.IsShineEffect(child))
                    continue;

                child.Draw(gameTime);
            }
        }

        // =====================================================================
        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || !_terrainPlacementReady) return;

            if (!RenderVisuals)
                return;

            var objects = Children;
            for (int i = 0; i < objects.Count; i++)
            {
                var child = objects[i];
                if (_visual.IsShineEffect(child))
                    continue;

                child.DrawAfter(gameTime);
            }
        }

        // =====================================================================
        public override void OnClick()
        {
            base.OnClick();
            if (_pickedUp) return;

            if (World is not Controls.WalkableWorldControl w || w.Walker == null) return;
            if (w.Walker.NetworkId != _mainPlayerId) return;

            float d = Vector3.Distance(w.Walker.Position, Position);
            if (d > PickupRange)
            {
                if (World.Scene is GameScene scene)
                {
                    scene.ChatLog?.AddMessage("System", "Item is too far away.", MessageType.System);
                }
                return;
            }

            // Stash the item data BEFORE sending the request
            CharacterState charState = MuGame.Network?.GetCharacterState();
            if (charState == null)
            {
                _log.LogError("OnClick: CharacterState is null, cannot stash item for pickup.");
                return;
            }

            charState.SetPendingPickupRawId(RawId);

            if (_scope is ItemScopeObject itemScope)
            {
                charState.StashPickedItem(itemScope.ItemData.ToArray());
            }
            else if (_scope is MoneyScopeObject moneyScope)
            {
                _log.LogInformation("OnClick: Pick up initiated for Money. Server will update Zen directly.");
            }
            else
            {
                _log.LogWarning("OnClick: Attempting to pick up unknown scope object type: {ScopeType}", _scope.ObjectType);
                return;
            }

            _pickedUp = true;

            _ = Task.Run(async () => await _charSvc.SendPickupItemRequestAsync(RawId, MuGame.Network.TargetVersion));
            _log.LogDebug("Pickup request sent for {RawId:X4} ({DisplayName})", RawId, DisplayName);
        }

        private string FormatItemDisplayName(string baseName, ItemDatabase.ItemDetails details)
        {
            var sb = new StringBuilder();

            if (details.IsExcellent) sb.Append("Excellent ");
            sb.Append(baseName);

            if (details.Level > 0) sb.Append($" +{details.Level}");
            if (details.OptionLevel > 0) sb.Append($" +Options{details.OptionLevel * 4}");
            if (details.HasLuck) sb.Append(" +Luck");
            if (details.HasSkill) sb.Append(" +Skill");

            return sb.ToString();
        }

        private int CalculateCoinCount(uint zenAmount)
        {
            int coinCount = (int)MathF.Sqrt(zenAmount) / 2;
            return Math.Clamp(coinCount, 3, MaxZenCoins);
        }

        private Color GetLabelColor(ScopeObject s, ItemDatabase.ItemDetails details)
        {
            if (s is MoneyScopeObject)
                return new Color(255, 204, 26);

            if (s is ItemScopeObject item && ItemDatabase.IsJewelItem(item.ItemData.Span))
                return new Color(255, 204, 26);

            if (details.IsAncient)
                return new Color(0, 255, 0);

            if (details.IsExcellent)
                return new Color(26, 255, 128);

            if (details.Level >= 7)
                return new Color(255, 204, 26);

            if (details.HasBlueOptions)
                return new Color(102, 179, 255);

            if (details.Level == 0)
                return new Color(179, 179, 179);

            if (details.Level < 3)
                return new Color(230, 230, 230);

            if (details.Level < 5)
                return new Color(255, 128, 51);

            return new Color(102, 179, 255);
        }

        public override void DrawHoverName()
        {
            if (!_terrainPlacementReady)
                return;

            if (_font == null)
                _font = GraphicsManager.Instance.Font;

            bool near = false;
            if (World is Controls.WalkableWorldControl w && w.Walker != null)
                near = Vector3.DistanceSquared(w.Walker.Position, Position) <= LabelVisibilityDistSq;

            if (!near || World?.Scene?.Status != GameControlStatus.Ready)
                return;

            // Dropped item labels are batched in BaseScene.Draw() with a single
            // Begin/End. Callers ensure a SpriteBatch is active before calling.
            if (!SpriteBatchScope.BatchIsBegun)
                return;

            Vector3 anchor = new(Position.X, Position.Y, BoundingBoxWorld.Max.Z + LabelOffsetZ);
            WorldLabelRenderer.DrawWorldLabel(
                GraphicsManager.Instance.Sprite,
                GraphicsDevice,
                _font,
                DisplayName,
                anchor,
                _labelColor,
                Color.Black * 0.55f,
                Camera.Instance.Projection,
                Camera.Instance.View,
                10f / Constants.BASE_FONT_SIZE * UiScaler.Scale * Constants.RENDER_SCALE);
        }

        public void ResetPickupState()
        {
            _pickedUp = false;
        }

        private void AttachShineEffect()
        {
            _visual.AttachShineEffect(this);
        }

        internal void DrawShineEffect(GameTime gameTime)
        {
            if (_terrainPlacementReady)
                _visual.DrawShineEffect(this, gameTime, _pickedUp, RenderVisuals);
        }

    }

    // Minimal model subclass used for dropped items
    internal class DroppedItemModel : ModelObject
    {
        protected override bool FreezeDynamicBuffersAfterFirstBuild => true;
        protected override bool AllowAnimationUpdates => false;
        protected override bool AllowLightingUpdates => false;
        protected override bool AllowDynamicLightingShader => false;

        public DroppedItemModel()
        {
            RenderShadow = false;
            // RenderPartObjectEffect sprites of special items (Devil's Key, potions, weapons, wings).
            Children.Add(new ItemLinkEffect { StandaloneItem = true });
        }

        public override void Update(GameTime gameTime)
        {
            ApplyItemObjectAttribute(gameTime.TotalGameTime.TotalMilliseconds);
            base.Update(gameTime);
        }

        protected override bool IsOverlayOnlyMesh(int mesh) => IsSpecialItemOverlayOnlyMesh(mesh);

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
            DrawSpecialItemPasses(gameTime);
        }
    }

    /// <summary>
    /// Renders the source client's Zen coin heap with one loaded BMD. The original client
    /// submits all coin transforms through BeginRenderCoinHeap; drawing the same model at
    /// each source position keeps the same layout without allocating one GPU buffer set per
    /// coin.
    /// </summary>
    internal sealed class DroppedZenModel : DroppedItemModel
    {
        private Vector3[] _coinPositions = Array.Empty<Vector3>();

        public BoundingBox PileBounds { get; private set; }

        public void ConfigureCoinLayout(int coinCount, ushort seed)
        {
            coinCount = Math.Clamp(coinCount, 3, 80);
            _coinPositions = new Vector3[coinCount];

            uint state = (uint)seed * 747796405u + 2891336453u;
            int maxRadius = coinCount + 20;
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            float coinRadius = 12f * Scale;
            float coinHeight = 4f * Scale;

            for (int i = 0; i < coinCount; i++)
            {
                int angleDegrees = NextValue(ref state, 360);
                int radius = NextValue(ref state, maxRadius);
                float angle = MathHelper.ToRadians(angleDegrees);
                Vector3 position = new Vector3(
                    MathF.Cos(angle) * radius,
                    MathF.Sin(angle) * radius,
                    0f);

                _coinPositions[i] = position;
                min = Vector3.Min(min, position - new Vector3(coinRadius, coinRadius, 0f));
                max = Vector3.Max(max, position + new Vector3(coinRadius, coinRadius, coinHeight));
            }

            PileBounds = new BoundingBox(
                new Vector3(min.X, min.Y, 0f),
                new Vector3(max.X, max.Y, MathF.Max(max.Z, 10f)));
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;

            Vector3 originalPosition = Position;
            for (int i = 0; i < _coinPositions.Length; i++)
            {
                Position = _coinPositions[i];
                base.Draw(gameTime);
            }

            Position = originalPosition;
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible)
                return;

            Vector3 originalPosition = Position;
            for (int i = 0; i < _coinPositions.Length; i++)
            {
                Position = _coinPositions[i];
                base.DrawAfter(gameTime);
            }

            Position = originalPosition;
        }

        private static int NextValue(ref uint state, int exclusiveMax)
        {
            state = state * 1664525u + 1013904223u;
            return (int)(state % (uint)exclusiveMax);
        }
    }
}
