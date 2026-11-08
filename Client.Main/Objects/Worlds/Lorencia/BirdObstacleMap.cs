using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;

namespace Client.Main.Objects.Worlds.Lorencia
{
    /// <summary>
    /// Coarse map of everything solid on the map (buildings, walls, trees, stones, fences): per terrain tile, the
    /// highest top of the objects' world bounding boxes. Birds read it to climb over or turn away from what is
    /// ahead and to find free ground to land on. Objects load and move in after the world, so it is rebuilt now and then.
    /// </summary>
    public sealed class BirdObstacleMap
    {
        public const float None = float.NegativeInfinity;

        private const int Size = 256;
        private const float MinObstacleHeight = 50f; // ground clutter below this matters neither to flying nor to landing
        private const float MaxObjectSpan = 3000f;   // bounding boxes wider than this are bad data, not buildings

        private readonly WalkableWorldControl _world;
        private readonly float[] _top = new float[Size * Size];

        public BirdObstacleMap(WalkableWorldControl world)
        {
            _world = world;
            Array.Fill(_top, None);
        }

        /// <summary>World Z of the highest obstacle top over the tile at (x, y), or <see cref="None"/>.</summary>
        public float TopAt(float x, float y)
        {
            int tx = (int)(x / Constants.TERRAIN_SCALE);
            int ty = (int)(y / Constants.TERRAIN_SCALE);
            if ((uint)tx >= Size || (uint)ty >= Size)
                return None;
            return _top[ty * Size + tx];
        }

        /// <summary>Map objects of the world, not the actors and not the birds themselves.</summary>
        internal static bool IsSolid(WorldObject obj) =>
            obj is ModelObject && obj is not WalkerObject && obj is not BirdObject &&
            obj.GetType().Namespace == typeof(BirdObstacleMap).Namespace &&
            obj.Status == GameControlStatus.Ready && !obj.Hidden &&
            obj.BoundingBoxWorld.Max.Z > obj.BoundingBoxWorld.Min.Z;

        public void Rebuild()
        {
            Array.Fill(_top, None);
            var terrain = _world.Terrain;
            const float scale = Constants.TERRAIN_SCALE;

            foreach (var obj in _world.Objects.GetSnapshotArray())
            {
                if (!IsSolid(obj))
                    continue;

                BoundingBox box = obj.BoundingBoxWorld;
                if (box.Max.X - box.Min.X > MaxObjectSpan || box.Max.Y - box.Min.Y > MaxObjectSpan)
                    continue;

                int x0 = Math.Clamp((int)(box.Min.X / scale), 0, Size - 1), x1 = Math.Clamp((int)(box.Max.X / scale), 0, Size - 1);
                int y0 = Math.Clamp((int)(box.Min.Y / scale), 0, Size - 1), y1 = Math.Clamp((int)(box.Max.Y / scale), 0, Size - 1);
                for (int ty = y0; ty <= y1; ty++)
                {
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        float ground = terrain.RequestTerrainHeight((tx + 0.5f) * scale, (ty + 0.5f) * scale);
                        if (box.Max.Z - ground <= MinObstacleHeight)
                            continue;

                        int index = ty * Size + tx;
                        if (box.Max.Z > _top[index])
                            _top[index] = box.Max.Z;
                    }
                }
            }
        }
    }
}
