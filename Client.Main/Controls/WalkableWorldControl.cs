using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Monsters;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Threading.Tasks;

namespace Client.Main.Controls
{
    /// <summary>
    /// Extends WorldControl to support click‐to‐move gameplay.
    /// </summary>
    public abstract class WalkableWorldControl : WorldControl
    {
        // --- Fields ---

        private CursorObject _cursor;
        private float _cursorNextMoveTime;
        private int _previousScrollValue;
        private float _targetCameraDistance;
        private int _lastMoveTargetX = -1;
        private int _lastMoveTargetY = -1;
        private float _cursorIntegrityCheckTimeMs;

        // --- Mouse tile caching for performance ---
        private Vector2 _lastMouseInBackBuffer = new Vector2(-1, -1);
        private Vector3 _lastCameraPosition;

        // --- Properties ---

        /// <summary>
        /// The player's walker object.
        /// </summary>
        public WalkerObject Walker { get; set; }

        /// <summary>
        /// The X coordinate of the tile currently under the mouse.
        /// </summary>
        public byte MouseTileX { get; set; } = 0;

        /// <summary>
        /// The Y coordinate of the tile currently under the mouse.
        /// </summary>
        public byte MouseTileY { get; set; } = 0;

        /// <summary>
        /// Height offset applied when placing the cursor above terrain.
        /// </summary>
        public float ExtraHeight { get; set; }

        public int HeroTile { get; private set; }

        // --- Constructors ---

        /// <summary>
        /// Initializes a walkable world with default walker.
        /// </summary>
        public WalkableWorldControl(short worldIndex)
            : base(worldIndex)
        {
            Interactive = true;
        }

        /// <summary>
        /// Initializes a walkable world with a specified walker.
        /// </summary>
        public WalkableWorldControl(short worldIndex, WalkerObject walker)
            : this(worldIndex)
        {
            Walker = walker;
        }

        // --- Lifecycle Methods ---

        public override async Task Load()
        {
            EnsureSingleCursorObject();
            _cursorIntegrityCheckTimeMs = 0f;
            await base.Load();
        }

        public override void Update(GameTime time)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            _cursorIntegrityCheckTimeMs -= (float)time.ElapsedGameTime.TotalMilliseconds;
            if (_cursorIntegrityCheckTimeMs <= 0f)
            {
                EnsureSingleCursorObject();
                _cursorIntegrityCheckTimeMs = 1000f;
            }

            HeroTile = World.Terrain.GetHeroTile(Walker.Position.X, Walker.Position.Y);

            // some UI overlay has the mouse, skip click-to-move this frame.
            if (Scene != null && Scene.MouseHoverControl != null && Scene.MouseHoverControl != Scene.World)
            {
                // a UI element has focus or mouse over, and it's not the world itself,
                // then the game world shouldn't process its specific click or scroll.
                // The IsMouseInputConsumedThisFrame flag further reinforces this for other inputs.
                base.Update(time);
                return;
            }

            CalculateMouseTilePos();

            // Handle click‐to‐move with a simple cooldown
            if (!Scene.IsMouseInputConsumedThisFrame && // check if UI already handled the click
                (Scene.MouseControl == this || Scene.MouseControl == World) && // ensure this world or its base is the target
                MuGame.Instance.Mouse.LeftButton == ButtonState.Pressed &&
                _cursorNextMoveTime <= 0f)
            {
                MonsterObject hoveredMonster = Walker is PlayerObject && !WorldHoverSystem.IsAltPressed()
                    ? WorldHoverSystem.FindBestLiveMonster(
                        VisibleObjects,
                        MuGame.Instance.MouseRay,
                        this)
                    : null;

                // If an NPC is under the cursor, consume click and don't trigger move
                if (Scene.MouseHoverObject is NPCObject && hoveredMonster == null)
                {
                    if (Scene is Client.Main.Scenes.BaseScene bs)
                        bs.SetMouseInputConsumed();
                    _cursorNextMoveTime = 250f;
                    return;
                }
                if (Walker is PlayerObject player)
                {
                    MonsterObject monster = hoveredMonster ?? FindMonsterAtTile(MouseTileX, MouseTileY);
                    if (monster != null)
                    {
                        float attackRange = player.GetAttackRangeTiles();
                        if (Vector2.Distance(player.Location, monster.Location) <= attackRange)
                        {
                            player.Attack(monster);
                            if (Scene is Client.Main.Scenes.BaseScene bs)
                                bs.SetMouseInputConsumed();
                            _cursorNextMoveTime = 250f;
                            return;
                        }
                    }
                }

                _cursorNextMoveTime = 250f;
                var newTile = new Vector2(MouseTileX, MouseTileY);

                // Prevent duplicate move marker placement while the mouse is held on the same tile.
                int targetX = (int)newTile.X;
                int targetY = (int)newTile.Y;
                if (targetX == _lastMoveTargetX && targetY == _lastMoveTargetY)
                    return;

                if (!IsWalkable(newTile))
                    return;

                // Don't allow movement if player is dead
                if (!Walker.IsAlive())
                    return;

                float worldX = newTile.X * Constants.TERRAIN_SCALE;
                float worldY = newTile.Y * Constants.TERRAIN_SCALE;
                float height = Terrain.RequestTerrainHeight(worldX, worldY) + ExtraHeight;
                _cursor.Position = new Vector3(worldX, worldY, height) + new Vector3(50f, 40f, 0);
                Walker.MoveTo(newTile);
                _lastMoveTargetX = targetX;
                _lastMoveTargetY = targetY;

                if (Scene is Client.Main.Scenes.BaseScene baseScene)
                    baseScene.SetMouseInputConsumed();
            }
            else if (_cursorNextMoveTime > 0f)
            {
                _cursorNextMoveTime -= (float)time.ElapsedGameTime.TotalMilliseconds;
            }

            if (MuGame.Instance.Mouse.LeftButton == ButtonState.Released)
            {
                _lastMoveTargetX = -1;
                _lastMoveTargetY = -1;
            }

            var mouseState = MuGame.Instance.Mouse;
            int currentScroll = mouseState.ScrollWheelValue;
            int scrollDiff = currentScroll - _previousScrollValue;
            if (scrollDiff != 0 && !Scene.IsMouseInputConsumedThisFrame) // check if UI already handled scroll
            {
                float zoomChange = scrollDiff / 100f * 100f;
                _targetCameraDistance = MathHelper.Clamp(
                    _targetCameraDistance - zoomChange,
                    100f, // Assuming 100f as the minimum camera distance
                    500f); // Assuming 500f as the maximum camera distance
            }
            _previousScrollValue = currentScroll;

            base.Update(time);
        }

        private void EnsureSingleCursorObject()
        {
            CursorObject primaryCursor = null;
            var objects = Objects.GetSnapshotArray();

            for (int i = objects.Length - 1; i >= 0; i--)
            {
                if (objects[i] is not CursorObject cursorObj)
                    continue;

                if (primaryCursor == null)
                {
                    primaryCursor = cursorObj;
                    continue;
                }

                Objects.Remove(cursorObj);
                if (cursorObj.Status != GameControlStatus.Disposed)
                    cursorObj.Dispose();
            }

            if (primaryCursor == null)
            {
                if (_cursor == null || _cursor.Status == GameControlStatus.Disposed)
                    _cursor = new CursorObject();

                if (!Objects.Contains(_cursor))
                    Objects.Add(_cursor);

                primaryCursor = _cursor;
            }

            _cursor = primaryCursor;
        }

        // --- Helper Methods ---

        /// <summary>
        /// Calculates which terrain tile is under the mouse cursor by raycasting.
        /// Uses caching when mouse is idle (not holding button) to save CPU.
        /// </summary>
        private void CalculateMouseTilePos()
        {
            // Use mouse position in back buffer space (handles fullscreen borderless scaling)
            var currentMousePos = MuGame.Instance.MouseInBackBuffer;
            var currentCamPos = Camera.Instance.Position;
            bool isButtonHeld = MuGame.Instance.Mouse.LeftButton == ButtonState.Pressed;

            // Use cache only when mouse button is NOT held and position hasn't changed
            // When button is held, always recalculate to support continuous movement
            if (!isButtonHeld &&
                currentMousePos == _lastMouseInBackBuffer &&
                currentCamPos == _lastCameraPosition)
            {
                return; // Use cached MouseTileX/MouseTileY values
            }

            _lastMouseInBackBuffer = currentMousePos;
            _lastCameraPosition = currentCamPos;

            // Create viewport from actual back buffer size (not GraphicsDevice.Viewport which may be stale
            // from render target usage during previous Draw() call)
            var gd = GraphicsManager.Instance.GraphicsDevice;
            var viewport = new Viewport(0, 0,
                gd.PresentationParameters.BackBufferWidth,
                gd.PresentationParameters.BackBufferHeight);

            var cam = Camera.Instance;
            var proj = cam.Projection;
            var view = cam.View;

            var near = viewport.Unproject(new Vector3(currentMousePos, 0f),
                                          proj,
                                          view,
                                          Matrix.Identity);
            var far = viewport.Unproject(new Vector3(currentMousePos, 1f),
                                          proj,
                                          view,
                                          Matrix.Identity);

            var ray = new Ray(near, Vector3.Normalize(far - near));

            // Optimized ray march: coarse step first, then refine on hit
            const float maxDistance = 5000f;
            float coarseStep = Constants.TERRAIN_SCALE; // 100f - faster scanning
            float fineStep = Constants.TERRAIN_SCALE / 10f; // 10f - precise hit detection
            float traveled = 0f;

            var lastPos = ray.Position;
            var lastDiff = lastPos.Z - Terrain.RequestTerrainHeight(lastPos.X, lastPos.Y) + ExtraHeight;
            bool hit = false;
            Vector3 hitPos = Vector3.Zero;

            while (traveled < maxDistance)
            {
                traveled += coarseStep;
                var pos = ray.Position + ray.Direction * traveled;
                float terrainZ = Terrain.RequestTerrainHeight(pos.X, pos.Y) + ExtraHeight;
                float diff = pos.Z - terrainZ;

                if (lastDiff > 0f && diff <= 0f)
                {
                    // Found crossing - refine within this segment
                    float segmentStart = traveled - coarseStep;
                    float refineTraveled = segmentStart;
                    var refineLastPos = ray.Position + ray.Direction * segmentStart;
                    float refineLastDiff = refineLastPos.Z - Terrain.RequestTerrainHeight(refineLastPos.X, refineLastPos.Y) + ExtraHeight;

                    while (refineTraveled < traveled)
                    {
                        refineTraveled += fineStep;
                        var refinePos = ray.Position + ray.Direction * refineTraveled;
                        float refineTerrainZ = Terrain.RequestTerrainHeight(refinePos.X, refinePos.Y) + ExtraHeight;
                        float refineDiff = refinePos.Z - refineTerrainZ;

                        if (refineLastDiff > 0f && refineDiff <= 0f)
                        {
                            float t = refineLastDiff / (refineLastDiff - refineDiff);
                            hitPos = Vector3.Lerp(refineLastPos, refinePos, t);
                            hit = true;
                            break;
                        }

                        refineLastPos = refinePos;
                        refineLastDiff = refineDiff;
                    }

                    if (hit)
                        break;
                }

                lastPos = pos;
                lastDiff = diff;
            }

            if (hit)
            {
                int gx = (int)(hitPos.X / Constants.TERRAIN_SCALE);
                int gy = (int)(hitPos.Y / Constants.TERRAIN_SCALE);

                MouseTileX = (byte)Math.Clamp(gx, 0, Constants.TERRAIN_SIZE - 1);
                MouseTileY = (byte)Math.Clamp(gy, 0, Constants.TERRAIN_SIZE - 1);
            }
            else
            {
                MouseTileX = 0;
                MouseTileY = 0;
            }
        }

        /// <summary>
        /// Returns the first <see cref="MonsterObject"/> occupying the given tile, or <c>null</c>.
        /// </summary>
        private MonsterObject FindMonsterAtTile(byte tileX, byte tileY)
        {
            var monsters = Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                if (m != null &&
                    !m.IsDead && !m.IsSummoned &&
                    m.Location.X == tileX &&
                    m.Location.Y == tileY)
                {
                    return m;
                }
            }
            return null;
        }
    }
}
