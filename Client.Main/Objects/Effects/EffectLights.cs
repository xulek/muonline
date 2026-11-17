#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controls;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Reusable dynamic-light slots for skill effects. Call <see cref="Set"/> every update while a slot
    /// should glow; a slot is registered with the terrain on its first visible use and stays registered
    /// (intensity 0 hides it without touching the light list) until <see cref="Clear"/> or
    /// <see cref="Dispose"/>. A shared per-frame budget caps how many effect lights can be registered at once,
    /// so mass casts degrade gracefully instead of flooding the light buffer.
    /// </summary>
    public sealed class EffectLights
    {
        private const float MinIntensity = 0.01f;
        private const double StaleSeconds = 0.2;

        // Instances with at least one registered slot; swept once per frame so a slot whose effect
        // stopped calling Set (early-return paths) cannot stay lit until dispose.
        private static readonly List<EffectLights> s_live = new(32);

        private static long s_frameId = -1;
        private static double s_now;
        private static int s_frameCount;
        private static int s_previousFrameCount;

        private readonly WorldObject _owner;
        private readonly DynamicLight?[] _lights;
        private readonly bool[] _registered;
        private readonly double[] _lastSet;
        private int _registeredCount;
        private TerrainControl? _terrain;

        public EffectLights(WorldObject owner, int slots)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _lights = new DynamicLight?[Math.Max(1, slots)];
            _registered = new bool[_lights.Length];
            _lastSet = new double[_lights.Length];
        }

        /// <summary>Maximum number of effect lights alive at the same time.</summary>
        public static int Budget => Constants.OPTIMIZE_FOR_INTEGRATED_GPU ? 12 : 32;

        public int Slots => _lights.Length;

        /// <summary>Updates one slot for this frame; intensity &lt;= 0 hides it.</summary>
        public void Set(int slot, Vector3 position, Vector3 color, float radius, float intensity)
        {
            if ((uint)slot >= (uint)_lights.Length)
                return;

            TerrainControl? terrain = _owner.World?.Terrain;
            if (terrain == null || !Constants.ENABLE_DYNAMIC_LIGHTS)
                return;

            bool visible = intensity > MinIntensity && radius > 1f;
            DynamicLight? light = _lights[slot];

            if (!_registered[slot] || !ReferenceEquals(_terrain, terrain))
            {
                if (!visible)
                    return;

                if (!TryReserveBudget())
                    return;

                light ??= _lights[slot] = new DynamicLight { Owner = _owner };
                if (_registered[slot])
                    _terrain?.RemoveDynamicLight(light);
                else
                    MarkRegistered(slot);
                _terrain = terrain;
                terrain.AddDynamicLight(light);
            }

            CountFrame();
            _lastSet[slot] = s_now;
            light!.Position = position;
            light.Color = color;
            light.Radius = MathF.Max(1f, radius);
            light.Intensity = visible ? intensity : 0f;
        }

        /// <summary>Hides a slot for this frame without unregistering it.</summary>
        public void Hide(int slot)
        {
            if ((uint)slot < (uint)_lights.Length && _registered[slot] && _lights[slot] != null)
                _lights[slot]!.Intensity = 0f;
        }

        /// <summary>Unregisters a slot (it may be set again later).</summary>
        public void Clear(int slot)
        {
            if ((uint)slot >= (uint)_lights.Length || !_registered[slot])
                return;

            _terrain?.RemoveDynamicLight(_lights[slot]!);
            _registered[slot] = false;
            if (--_registeredCount == 0)
                s_live.Remove(this);
        }

        private void MarkRegistered(int slot)
        {
            _registered[slot] = true;
            if (_registeredCount++ == 0)
                s_live.Add(this);
        }

        public void Dispose()
        {
            for (int i = 0; i < _lights.Length; i++)
                Clear(i);
            _terrain = null;
        }

        // ----------------------------------------------------------------- envelopes

        /// <summary>0..1 fade-in over [0, attack], hold, fade-out over [duration - release, duration].</summary>
        public static float Envelope(float age, float duration, float attack, float release)
        {
            if (age <= 0f || age >= duration)
                return 0f;
            float a = attack > 0f ? MathHelper.Clamp(age / attack, 0f, 1f) : 1f;
            float r = release > 0f ? MathHelper.Clamp((duration - age) / release, 0f, 1f) : 1f;
            return MathF.Min(a, r);
        }

        /// <summary>Fast flash that decays: 1 at t=0, 0 at t=duration, curved.</summary>
        public static float Flash(float age, float duration, float power = 1.6f)
        {
            if (age < 0f || age >= duration || duration <= 0f)
                return 0f;
            return MathF.Pow(1f - age / duration, power);
        }

        /// <summary>Cheap organic flicker around 1 (amount 0..1).</summary>
        public static float Flicker(float time, float seed, float amount = 0.15f)
        {
            float n = MathF.Sin(time * 17.3f + seed) * 0.6f + MathF.Sin(time * 31.7f + seed * 2.1f) * 0.4f;
            return 1f + n * amount;
        }

        private static bool TryReserveBudget()
        {
            SyncFrame();
            // Lights counted last frame plus those already set this frame approximate the live total.
            return Math.Max(s_previousFrameCount, s_frameCount) < Budget;
        }

        private static void SweepStale()
        {
            for (int i = 0; i < s_live.Count; i++)
            {
                EffectLights fx = s_live[i];
                for (int s = 0; s < fx._lights.Length; s++)
                {
                    if (fx._registered[s] && s_now - fx._lastSet[s] > StaleSeconds)
                        fx._lights[s]!.Intensity = 0f;
                }
            }
        }

        private static void CountFrame()
        {
            SyncFrame();
            s_frameCount++;
        }

        private static void SyncFrame()
        {
            long id = MuGame.FrameIndex;
            if (id != s_frameId)
            {
                s_frameId = id;
                s_previousFrameCount = s_frameCount;
                s_frameCount = 0;
                s_now = MuGame.Instance?.GameTime?.TotalGameTime.TotalSeconds ?? 0d;
                SweepStale();
            }
        }
    }
}
