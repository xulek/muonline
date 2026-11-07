using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Client.Main.Diagnostics
{
    /// <summary>
    /// Diagnostics only: device draws, time and calls of the model draw paths (shader kind, pass and route),
    /// to see which kind of submission the frame is made of. Active together with <see cref="ObjectTypeProfiler"/>.
    /// </summary>
    public static class DrawPathCounter
    {
        public readonly record struct Token(long Timestamp, long Draws, long Shaders);

        public const int ShaderKinds = 4;
        private static readonly string[] ShaderKindNames = { "AlphaTest", "DynLight", "ItemMat", "MonsterMat" };
        private static readonly string[] RouteNames = { "GpuBatch", "FastAlphaBatch", "PerMesh" };

        public enum Extra { Composite, CompositeDraw, ShadowAndHighlight, ActorShadowFlush, SpecialPasses, ChromeUnderlay, Count }

        // [shader kind][after pass][route] then the extra slots.
        private const int MeshSlots = ShaderKinds * 2 * 3;
        private static readonly long[] Draws = new long[MeshSlots + (int)Extra.Count];
        private static readonly long[] Ticks = new long[MeshSlots + (int)Extra.Count];
        private static readonly long[] Calls = new long[MeshSlots + (int)Extra.Count];
        private static readonly long[] Shaders = new long[MeshSlots + (int)Extra.Count];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Token Start() =>
            ObjectTypeProfiler.Enabled ? new Token(Stopwatch.GetTimestamp(), CurrentDrawCount(), CurrentShaderCount()) : default;

        public static void StopMesh(Token token, int shaderKind, bool afterPass, int route) =>
            Add(token, (shaderKind * 2 + (afterPass ? 1 : 0)) * 3 + route);

        public static void StopExtra(Token token, Extra extra) => Add(token, MeshSlots + (int)extra);

        private static void Add(Token token, int slot)
        {
            if (token.Timestamp == 0 || (uint)slot >= (uint)Draws.Length)
                return;
            Ticks[slot] += Stopwatch.GetTimestamp() - token.Timestamp;
            Draws[slot] += Math.Max(0, CurrentDrawCount() - token.Draws);
            Shaders[slot] += Math.Max(0, CurrentShaderCount() - token.Shaders);
            Calls[slot]++;
        }

        private static long CurrentDrawCount() =>
            Controllers.GraphicsManager.Instance?.GraphicsDevice?.Metrics.DrawCount ?? 0;

        private static long CurrentShaderCount() =>
            Controllers.GraphicsManager.Instance?.GraphicsDevice?.Metrics.VertexShaderCount ?? 0;

        /// <summary>Per-frame averages ("path=ms/calls/draws"), largest time first; then resets.</summary>
        public static string Take(int frames)
        {
            frames = Math.Max(1, frames);
            var order = new int[Draws.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            Array.Sort(order, (a, b) => Ticks[b].CompareTo(Ticks[a]));

            double msPerTick = 1000.0 / Stopwatch.Frequency / frames;
            var sb = new StringBuilder();
            foreach (int slot in order)
            {
                if (Calls[slot] == 0)
                    continue;
                sb.Append(SlotName(slot)).Append('=')
                  .Append((Ticks[slot] * msPerTick).ToString("F2", CultureInfo.InvariantCulture)).Append('/')
                  .Append(Calls[slot] / frames).Append('/').Append(Draws[slot] / frames)
                  .Append('/').Append(Shaders[slot] / frames).Append(' ');
            }

            Array.Clear(Draws);
            Array.Clear(Shaders);
            Array.Clear(Ticks);
            Array.Clear(Calls);
            return sb.ToString();
        }

        private static string SlotName(int slot)
        {
            if (slot >= MeshSlots)
                return ((Extra)(slot - MeshSlots)).ToString();
            int route = slot % 3;
            int pass = (slot / 3) % 2;
            int kind = slot / 6;
            return $"{ShaderKindNames[kind]}{(pass == 1 ? "After" : string.Empty)}.{RouteNames[route]}";
        }
    }
}
