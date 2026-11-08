using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Client.Main.Objects.Worlds.Lorencia
{
    // Diagnostics for the StressHarness (MU_STRESS_BIRDS=1). Nothing here runs in the game.
    public partial class BirdManager
    {
        private WorldObject[] _diagSolids;
        private int _diagSolidsAge, _diagFrames, _diagGroundSamples;
        private long _diagBirdFrames, _diagInsideAir, _diagInsideGround;
        private float _diagClearanceMin = float.MaxValue, _diagClearanceMax = float.MinValue, _diagInsideAltitude;
        private readonly System.Collections.Generic.Dictionary<string, int> _diagInsideKinds = new();

        /// <summary>Flock size, and how many birds are over town or walls.</summary>
        internal string DescribeForDiagnostics()
        {
            int hidden = 0, overTown = 0, overWalls = 0;
            foreach (var bird in _birds)
            {
                if (bird.Hidden)
                    hidden++;
                int x = (int)(bird.Position.X / Constants.TERRAIN_SCALE);
                int y = (int)(bird.Position.Y / Constants.TERRAIN_SCALE);
                if (x < 0 || x >= 256 || y < 0 || y >= 256)
                    continue;
                var flag = _world.Terrain.RequestTerrainFlag(x, y);
                if (flag.HasFlag(Client.Data.ATT.TWFlags.SafeZone))
                    overTown++;
                if (flag.HasFlag(Client.Data.ATT.TWFlags.NoMove))
                    overWalls++;
            }
            return $"birds={_birds.Count} hidden={hidden} overSafeZone={overTown} overNoMove={overWalls}";
        }

        /// <summary>Sends flying birds down onto the ground around a point.</summary>
        internal void LandNear(Vector3 center, int count)
        {
            int placed = 0;
            foreach (var bird in _birds)
            {
                if (placed >= count)
                    break;
                if (bird.AIState != BirdObject.BirdAIState.Flying)
                    continue;

                float angle = placed * 2.1f, distance = 300f + 90f * placed;
                float x = center.X + MathF.Cos(angle) * distance, y = center.Y + MathF.Sin(angle) * distance;
                float z = _world.Terrain.RequestTerrainHeight(x, y) + 350f;
                bird.Hidden = false;
                bird.Position = new Vector3(x, y, z);
                bird.DiagnosticsDescend();
                placed++;
            }
        }

        private static bool IsDiagnosticSolid(WorldObject obj) =>
            obj is ModelObject && obj is not WalkerObject && obj is not BirdObject &&
            obj.Status == GameControlStatus.Ready && !obj.Hidden &&
            obj.BoundingBoxWorld.Max.Z > obj.BoundingBoxWorld.Min.Z;

        /// <summary>Called every frame: counts birds inside the bounding box of any map object, and how far the
        /// lowest point of a bird on the ground is from the terrain (0 = standing on it).</summary>
        internal void SampleForDiagnostics()
        {
            if (_diagSolids == null || ++_diagSolidsAge >= 120)
            {
                _diagSolidsAge = 0;
                _diagSolids = _world.Objects.GetSnapshotArray().Where(IsDiagnosticSolid).ToArray();
            }

            _diagFrames++;
            foreach (var bird in _birds)
            {
                if (bird.Hidden)
                    continue;

                _diagBirdFrames++;
                Vector3 p = bird.Position;
                WorldObject hit = null;
                foreach (var solid in _diagSolids)
                {
                    if (solid.BoundingBoxWorld.Contains(p) == ContainmentType.Contains)
                    {
                        hit = solid;
                        break;
                    }
                }

                bool grounded = bird.AIState == BirdObject.BirdAIState.OnGround;
                if (hit != null)
                {
                    if (grounded) _diagInsideGround++;
                    else _diagInsideAir++;

                    float above = p.Z - _world.Terrain.RequestTerrainHeight(p.X, p.Y);
                    string key = $"{hit.GetType().Name}/{bird.AIState}/{(bird.Age < 4f ? "young" : "old")}/{(bird.IsAvoiding ? "turning" : "straight")}/top+{hit.BoundingBoxWorld.Max.Z - _world.Terrain.RequestTerrainHeight(hit.Position.X, hit.Position.Y):F0}";
                    _diagInsideKinds[key] = _diagInsideKinds.GetValueOrDefault(key) + 1;
                    _diagInsideAltitude = MathF.Max(_diagInsideAltitude, above);
                }

                if (grounded)
                {
                    float clearance = bird.MeasureLowestWorldZ() - _world.Terrain.RequestTerrainHeight(p.X, p.Y);
                    _diagClearanceMin = MathF.Min(_diagClearanceMin, clearance);
                    _diagClearanceMax = MathF.Max(_diagClearanceMax, clearance);
                    _diagGroundSamples++;
                }
            }
        }

        internal string DescribeSamplesForDiagnostics() =>
            $"frames={_diagFrames} birdFrames={_diagBirdFrames} insideObjectInAir={_diagInsideAir} insideObjectOnGround={_diagInsideGround} " +
            $"groundSamples={_diagGroundSamples} feetAboveTerrain=[{_diagClearanceMin:F1}..{_diagClearanceMax:F1}]";

        /// <summary>What the birds were inside of: object / bird state / age / turning / object top above terrain.</summary>
        internal string DescribeInsideForDiagnostics() =>
            string.Join("; ", _diagInsideKinds.OrderByDescending(p => p.Value).Take(14).Select(p => $"{p.Key}={p.Value}")) +
            $" maxAltitudeInside={_diagInsideAltitude:F0}";

        /// <summary>Map object types birds could hit: count and tallest top above the terrain.</summary>
        internal string DescribeObstacleTypesForDiagnostics()
        {
            if (_diagSolids == null)
                return "none";

            return string.Join(", ", _diagSolids
                .GroupBy(o => o.GetType().Name)
                .OrderByDescending(g => g.Count())
                .Take(24)
                .Select(g => $"{g.Key}:{g.Count()}(top+{g.Max(o => o.BoundingBoxWorld.Max.Z - _world.Terrain.RequestTerrainHeight(o.Position.X, o.Position.Y)):F0})"));
        }
    }
}
