using Client.Main.Controls;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Core.Utilities
{
    public class PathNode
    {
        public Vector2 Position { get; private set; }
        public PathNode Parent { get; set; }
        public float GCost { get; set; } // Cost from the start
        public float HCost { get; set; } // Heuristic to the goal
        public float FCost => GCost + HCost;

        public PathNode(Vector2 position)
        {
            Reset(position);
        }

        public void Reset(Vector2 position)
        {
            Position = position;
            GCost = float.MaxValue;
            HCost = 0;
            Parent = null;
        }
    }

    public static class Pathfinding
    {
        // Reused direction offsets to avoid per-call array allocations in GetNeighbors
        private static readonly Vector2[] Directions = new[]
        {
            new Vector2(0, -1),  // North
            new Vector2(0, 1),   // South
            new Vector2(-1, 0),  // West
            new Vector2(1, 0),   // East
            new Vector2(-1, -1), // Northwest
            new Vector2(1, -1),  // Northeast
            new Vector2(-1, 1),  // Southwest
            new Vector2(1, 1)    // Southeast
        };

        // Thread-local buffers to avoid per-call allocations in A*
        private static readonly ThreadLocal<PathfindingContext> _ctx = new(() => new PathfindingContext());
        private static readonly SemaphoreSlim _workerGate = new(Math.Clamp(Environment.ProcessorCount / 4, 1, 2));

        public static List<Vector2> FindPath(Vector2 start, Vector2 goal, WorldControl world)
            => FindPath(start, goal, world, CancellationToken.None);

        public static Task<List<Vector2>> FindPathAsync(
            Vector2 start,
            Vector2 goal,
            WorldControl world,
            CancellationToken cancellationToken)
            => RunBoundedAsync(
                token => FindPath(start, goal, world, token),
                cancellationToken);

        /// <summary>
        /// Executes a CPU pathfinding job through the shared bounded worker gate. Compound
        /// searches (for example, finding an accessible tile around an NPC) use this method so
        /// they cannot bypass the global pathfinding concurrency limit with a raw Task.Run.
        /// </summary>
        public static async Task<T> RunBoundedAsync<T>(
            Func<CancellationToken, T> work,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(work);

            // Cancellation is an expected result when a newer movement request supersedes
            // the current one. Avoid propagating a canceled Task/OperationCanceledException,
            // because debuggers treat the cooperative cancellation as an unhandled user-code
            // exception even though callers catch it later.
            bool enteredGate = false;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (await _workerGate.WaitAsync(16).ConfigureAwait(false))
                    {
                        enteredGate = true;
                        break;
                    }
                }

                if (!enteredGate || cancellationToken.IsCancellationRequested)
                    return default;

                // Do not pass the token to Task.Run. The worker observes it cooperatively and
                // returns default instead of creating a canceled Task that throws on await.
                return await Task.Run(() => work(cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return default;
            }
            finally
            {
                if (enteredGate)
                    _workerGate.Release();
            }
        }

        public static List<Vector2> FindPath(
            Vector2 start,
            Vector2 goal,
            WorldControl world,
            CancellationToken cancellationToken)
            => FindPath(start, goal, world, cancellationToken, int.MaxValue);

        /// <summary>
        /// A* limited to <paramref name="maxExpandedNodes"/> expansions; returns null when the goal is not reached
        /// within the budget (an unreachable goal would otherwise flood the whole 256 x 256 map).
        /// </summary>
        public static List<Vector2> FindPath(
            Vector2 start,
            Vector2 goal,
            WorldControl world,
            CancellationToken cancellationToken,
            int maxExpandedNodes)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (cancellationToken.IsCancellationRequested)
                return null;

            var ctx = _ctx.Value;
            ctx.Clear();
            int expandedNodes = 0;

            try
            {
                PathNode startNode = ctx.GetOrCreateNode(start);
                PathNode goalNode = ctx.GetOrCreateNode(goal);

                startNode.GCost = 0;
                startNode.HCost = Heuristic(start, goal);
                ctx.OpenSet.Enqueue(startNode, startNode.FCost);

                while (ctx.OpenSet.Count > 0)
                {
                    if ((expandedNodes++ & 63) == 0 && cancellationToken.IsCancellationRequested)
                        return null;

                    if (expandedNodes > maxExpandedNodes)
                        return null;

                    PathNode currentNode = ctx.OpenSet.Dequeue();

                    if (currentNode.Position == goalNode.Position)
                        return RetracePath(startNode, currentNode);

                    if (!ctx.ClosedSet.Add(currentNode.Position))
                        continue;

                    // Avoid the iterator allocation previously created for every expanded node.
                    for (int directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                    {
                        Vector2 neighborPos = currentNode.Position + Directions[directionIndex];
                        if (!IsWithinMapBounds(neighborPos, world) || !world.IsWalkable(neighborPos))
                            continue;

                        if (ctx.ClosedSet.Contains(neighborPos))
                            continue;

                        PathNode neighborNode = ctx.GetOrCreateNode(neighborPos);
                        float tentativeGCost = currentNode.GCost + Distance(currentNode.Position, neighborPos);

                        if (tentativeGCost < neighborNode.GCost)
                        {
                            neighborNode.GCost = tentativeGCost;
                            neighborNode.HCost = Heuristic(neighborPos, goal);
                            neighborNode.Parent = currentNode;
                            ctx.OpenSet.Enqueue(neighborNode, neighborNode.FCost);
                        }
                    }
                }

                return null;
            }
            finally
            {
                ctx.ReleaseNodes();
            }
        }

        private static float Heuristic(Vector2 a, Vector2 b)
        {
            return Vector2.Distance(a, b);
        }

        private static float Distance(Vector2 a, Vector2 b)
        {
            return Vector2.Distance(a, b);
        }

        private static bool IsWithinMapBounds(Vector2 position, WorldControl world)
        {
            return position.X >= 0 && position.X < Constants.TERRAIN_SIZE &&
                   position.Y >= 0 && position.Y < Constants.TERRAIN_SIZE;
        }

        private static List<Vector2> RetracePath(PathNode startNode, PathNode endNode)
        {
            List<Vector2> path = new();
            PathNode currentNode = endNode;

            while (currentNode != null && currentNode != startNode)
            {
                path.Add(currentNode.Position);
                currentNode = currentNode.Parent;
            }

            path.Reverse(); // Reverse the path to go from start to goal
            return path;
        }

        /// <summary>
        /// True when every tile of <see cref="BuildDirectPath"/> from start to goal is walkable (no allocation).
        /// </summary>
        public static bool IsDirectPathWalkable(Vector2 start, Vector2 goal, WorldControl world)
        {
            Vector2 current = start;
            while (current != goal)
            {
                if (current.X != goal.X)
                    current.X += (float)Math.Sign(goal.X - current.X);
                if (current.Y != goal.Y)
                    current.Y += (float)Math.Sign(goal.Y - current.Y);
                if (!IsWithinMapBounds(current, world) || !world.IsWalkable(current))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Builds a simple straight-line path ignoring obstacles. Used as a
        /// fallback when A* fails but we still need visible movement.
        /// </summary>
        public static List<Vector2> BuildDirectPath(Vector2 start, Vector2 goal)
        {
            List<Vector2> path = new();
            Vector2 current = start;
            while (current != goal)
            {
                if (current.X != goal.X)
                    current.X += (float)Math.Sign(goal.X - current.X);
                if (current.Y != goal.Y)
                    current.Y += (float)Math.Sign(goal.Y - current.Y);
                path.Add(current);
            }

            return path;
        }

        private sealed class PathfindingContext
        {
            public PriorityQueue<PathNode, float> OpenSet { get; } = new();
            public Dictionary<Vector2, PathNode> AllNodes { get; } = new();
            public HashSet<Vector2> ClosedSet { get; } = new();
            private readonly Stack<PathNode> _nodePool = new();

            public void Clear()
            {
                OpenSet.Clear();
                ClosedSet.Clear();
                // AllNodes cleared in ReleaseNodes
            }

            public PathNode GetOrCreateNode(Vector2 position)
            {
                if (AllNodes.TryGetValue(position, out PathNode node))
                {
                    return node;
                }

                node = _nodePool.Count > 0 ? _nodePool.Pop() : new PathNode(position);
                node.Reset(position);
                AllNodes[position] = node;
                return node;
            }

            public void ReleaseNodes()
            {
                foreach (var node in AllNodes.Values)
                {
                    _nodePool.Push(node);
                }
                AllNodes.Clear();
            }
        }
    }
}
