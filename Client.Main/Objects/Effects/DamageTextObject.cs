using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Client.Main.Scenes;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Crisp, dynamic MMORPG combat number with a readable linger.
    ///
    /// The effect uses a sharp impact pop, a directional burst, and a brief coast.
    /// It deliberately avoids outlines, white overlays, per-glyph rotations,
    /// and non-uniform font scaling because those operations produce halos
    /// and unstable edges on rasterized SpriteFont glyphs.
    /// </summary>
    public class DamageTextObject : EffectObject
    {
        public string Text { get; private set; }
        public Color TextColor { get; private set; }
        public ushort TargetId { get; private set; }

        private const float NormalLifetime = 0.82f;
        private const float CriticalLifetime = 1.02f;

        private const float NormalFontSize = 15.5f;
        private const float CriticalFontSize = 20.0f;

        private const float NormalFadeStart = 0.79f;
        private const float CriticalFadeStart = 0.82f;

        private const float PlayerHeadBoneTextOffsetZ = 18f;
        private const float PlayerFallbackHeight = 142f;
        private const float MonsterTopInset = 12f;

        private const int MaxPoolSize = 256;

        private static readonly ConcurrentBag<DamageTextObject> Pool = new();

        // Alternating burst lanes keep rapid hits readable without building
        // one tall column of numbers above the target.
        private static readonly Vector2[] BurstDirections =
        {
            new Vector2(0.92f, -0.38f),
            new Vector2(-0.92f, -0.38f),
            new Vector2(0.72f, -0.68f),
            new Vector2(-0.72f, -0.68f),
            new Vector2(1.00f, -0.18f),
            new Vector2(-1.00f, -0.18f),
            new Vector2(0.52f, -0.86f),
            new Vector2(-0.52f, -0.86f)
        };

        private static readonly Vector2[] SpawnOffsets =
        {
            new Vector2(3f, -2f),
            new Vector2(-3f, -2f),
            new Vector2(5f, -6f),
            new Vector2(-5f, -6f),
            new Vector2(7f, 1f),
            new Vector2(-7f, 1f),
            new Vector2(1f, -9f),
            new Vector2(-1f, -9f)
        };

        private static int _poolCount;
        private static int _sequence;

        private float _elapsed;
        private float _lifetime;
        private float _progress;
        private float _opacity;
        private float _burstDistance;
        private float _arcHeight;
        private float _seed;

        private int _laneIndex;

        private bool _isCritical;
        private bool _anchorCaptured;
        private bool _recycled;

        private Vector2 _burstDirection;
        private Vector2 _spawnOffset;
        private Vector2 _screenPosition;
        private Vector3 _worldImpactPoint;
        private Color _sourceColor;

        public static int PoolCount => Volatile.Read(ref _poolCount);

        /// <summary>Run on the graphics thread while the loading screen is active.</summary>
        public static void PrewarmRendering()
        {
            // SpriteFont is already loaded, but the first combat number still pays for
            // JIT and lazy draw-path setup. Exercise both variants outside the viewport;
            // do not switch render targets (which could discard the loading frame).
            using var sample = new DamageTextObject("0123456789!!! Miss", 0, Color.DeepSkyBlue);
            sample._screenPosition = new Vector2(-16384f, -16384f);
            var graphicsDevice = GraphicsManager.Instance.GraphicsDevice;
            var previousBlend = graphicsDevice.BlendState;
            try
            {
                var gameTime = new GameTime();
                sample.DrawAfter(gameTime);
                sample._isCritical = false;
                sample.DrawAfter(gameTime);
            }
            finally
            {
                graphicsDevice.BlendState = previousBlend;
            }
        }

        public DamageTextObject(string text, ushort targetId, Color color)
        {
            Reset(text, targetId, color);
        }

        public static DamageTextObject Rent(string text, ushort targetId, Color color)
        {
            if (Constants.ENABLE_EFFECT_POOLING && Pool.TryTake(out DamageTextObject instance))
            {
                Interlocked.Decrement(ref _poolCount);
                instance.Reset(text, targetId, color);
                return instance;
            }

            return new DamageTextObject(text, targetId, color);
        }

        public void Recycle()
        {
            if (_recycled)
                return;

            _recycled = true;

            try
            {
                Dispose();
            }
            finally
            {
                if (Constants.ENABLE_EFFECT_POOLING &&
                    Interlocked.Increment(ref _poolCount) <= MaxPoolSize)
                {
                    Pool.Add(this);
                }
                else
                {
                    Interlocked.Decrement(ref _poolCount);
                }
            }
        }

        private void Reset(string text, ushort targetId, Color color)
        {
            Text = text ?? string.Empty;
            TextColor = color;
            TargetId = targetId;
            _sourceColor = color;

            _isCritical = DetectCriticalHit(Text, color);
            _lifetime = _isCritical ? CriticalLifetime : NormalLifetime;

            int sequence = Interlocked.Increment(ref _sequence) & int.MaxValue;
            _laneIndex = sequence % BurstDirections.Length;
            _burstDirection = Vector2.Normalize(BurstDirections[_laneIndex]);
            _spawnOffset = SpawnOffsets[_laneIndex];

            _burstDistance = RandomRange(
                _isCritical ? 52f : 38f,
                _isCritical ? 66f : 49f);

            _arcHeight = RandomRange(
                _isCritical ? 13f : 8f,
                _isCritical ? 18f : 13f);

            _seed = RandomRange(0f, MathHelper.TwoPi);

            _elapsed = 0f;
            _progress = 0f;
            _opacity = 1f;
            _anchorCaptured = false;
            _recycled = false;
            _worldImpactPoint = Vector3.Zero;
            _screenPosition = Vector2.Zero;

            Alpha = 1f;
            Scale = 1f;
            IsTransparent = true;
            AffectedByTransparency = false;
            Status = GameControlStatus.Ready;
            Hidden = false;
        }

        public override Task Load()
        {
            Status = GameControlStatus.Ready;
            return Task.CompletedTask;
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || _recycled)
                return;

            float delta = Math.Max(0f, (float)gameTime.ElapsedGameTime.TotalSeconds);
            _elapsed += delta;

            if (_elapsed >= _lifetime)
            {
                Deactivate();
                return;
            }

            if (!_anchorCaptured)
            {
                WalkerObject target = ResolveTarget();
                if (target == null || target.Hidden || target.Status == GameControlStatus.Disposed)
                {
                    Deactivate();
                    return;
                }

                // Capture once so the number behaves like an impact effect instead
                // of following every animation movement of the target's head.
                _worldImpactPoint = CalculateImpactPoint(target);
                _anchorCaptured = true;
            }

            Vector3 projected = GraphicsDevice.Viewport.Project(
                _worldImpactPoint,
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            if (projected.Z < 0f || projected.Z > 1f)
            {
                Hidden = true;
                return;
            }

            Hidden = false;

            Point virtualPoint = UiScaler.ToVirtual(
                new Point((int)projected.X, (int)projected.Y));

            Vector2 anchor = new Vector2(virtualPoint.X, virtualPoint.Y);
            _progress = MathHelper.Clamp(_elapsed / _lifetime, 0f, 1f);
            _opacity = CalculateOpacity(_progress);
            _screenPosition = anchor + CalculateMotion(_progress);

            // Keep EffectObject state synchronized for systems that inspect Alpha.
            Alpha = _opacity;

            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            // Screen-space rendering is performed in DrawAfter.
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || Hidden || _recycled || _opacity <= 0.001f || string.IsNullOrEmpty(Text))
                return;

            SpriteBatch spriteBatch = GraphicsManager.Instance.Sprite;
            SpriteFont font = GraphicsManager.Instance.Font;
            if (spriteBatch == null || font == null)
                return;

            bool ownScope = !SpriteBatchScope.BatchIsBegun;
            SpriteBatchScope scope = default;

            if (ownScope)
            {
                scope = new SpriteBatchScope(
                    spriteBatch,
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    UiScaler.SpriteTransform);
            }

            try
            {
                DrawCombatNumber(spriteBatch, font);
            }
            finally
            {
                if (ownScope)
                    scope.Dispose();
            }
        }

        private void DrawCombatNumber(SpriteBatch spriteBatch, SpriteFont font)
        {
            float fontSize = _isCritical ? CriticalFontSize : NormalFontSize;
            float baseScale = fontSize / Constants.BASE_FONT_SIZE;
            float animationScale = CalculateScale(_progress);
            float scale = Math.Max(0.05f, baseScale * animationScale);
            float rotation = CalculateRotation(_progress);

            Vector2 origin = font.MeasureString(Text) * 0.5f;
            Color mainColor = ApplyOpacity(CalculateColor(_progress), _opacity);

            // A short directional after-image communicates impact velocity.
            // It is behind the number, never larger than it, and uses correctly
            // premultiplied opacity, so it cannot form a bright halo.
            float trailStrength = 1f - SmoothStep(0.02f, _isCritical ? 0.20f : 0.16f, _progress);
            if (trailStrength > 0.01f)
            {
                int trailCount = _isCritical ? 2 : 1;

                for (int i = trailCount; i >= 1; i--)
                {
                    float distance = i * (_isCritical ? 4.0f : 3.0f);
                    float trailOpacity = _opacity * trailStrength *
                        (_isCritical ? 0.11f : 0.08f) / i;

                    DrawText(
                        spriteBatch,
                        font,
                        Text,
                        _screenPosition - _burstDirection * distance,
                        ApplyOpacity(_sourceColor, trailOpacity),
                        origin,
                        scale,
                        rotation);
                }
            }

            // One compact dark shadow provides contrast without behaving like an outline.
            float shadowOpacity = _opacity * (_isCritical ? 0.48f : 0.38f);
            Vector2 shadowOffset = new Vector2(
                1.15f - _burstDirection.X * 0.35f,
                1.55f);

            DrawText(
                spriteBatch,
                font,
                Text,
                _screenPosition + shadowOffset,
                ApplyOpacity(Color.Black, shadowOpacity),
                origin,
                scale,
                rotation);

            DrawText(
                spriteBatch,
                font,
                Text,
                _screenPosition,
                mainColor,
                origin,
                scale,
                rotation);
        }

        private Vector2 CalculateMotion(float progress)
        {
            float impact = Normalize(progress, 0f, _isCritical ? 0.15f : 0.12f);

            // A tiny opposite recoil makes the following burst feel more forceful.
            float recoil = 1f - EaseOutCubic(impact);
            Vector2 recoilOffset = -_burstDirection * recoil * (_isCritical ? 5.5f : 3.5f);

            // Most of the distance is covered immediately. The final part only coasts,
            // which keeps the effect fast and readable during high attack speed combat.
            float burst = EaseOutExpo(Normalize(
                progress,
                _isCritical ? 0.025f : 0.020f,
                _isCritical ? 0.52f : 0.48f));

            float coast = EaseOutCubic(Normalize(progress, 0.46f, 1f));
            float distance = _burstDistance * burst + (_isCritical ? 7f : 4f) * coast;
            Vector2 directionalOffset = _burstDirection * distance;

            // A shallow impact arc adds weight but never becomes classic floating text.
            float arcTime = Normalize(progress, 0.04f, 0.84f);
            float arc = -(float)Math.Sin(arcTime * MathHelper.Pi) * _arcHeight;

            Vector2 snap = Vector2.Zero;
            if (_isCritical && impact < 1f)
            {
                float energy = 1f - impact;
                snap.X = (float)Math.Sin(_elapsed * 105f + _seed) * 1.35f * energy;
                snap.Y = (float)Math.Cos(_elapsed * 83f + _seed) * 0.75f * energy;
            }

            return _spawnOffset + recoilOffset + directionalOffset + new Vector2(0f, arc) + snap;
        }

        private float CalculateScale(float progress)
        {
            float startScale = _isCritical ? 0.58f : 0.68f;
            float peakScale = _isCritical ? 1.42f : 1.24f;
            float settledScale = _isCritical ? 1.08f : 1.00f;

            float peakEnd = _isCritical ? 0.10f : 0.09f;
            float settleEnd = _isCritical ? 0.30f : 0.27f;

            if (progress < peakEnd)
            {
                float phase = Normalize(progress, 0f, peakEnd);
                return MathHelper.Lerp(startScale, peakScale, EaseOutBack(phase));
            }

            if (progress < settleEnd)
            {
                float phase = Normalize(progress, peakEnd, settleEnd);
                return MathHelper.Lerp(peakScale, settledScale, EaseOutCubic(phase));
            }

            // A very small contraction during the fade avoids a frozen-looking ending.
            float finish = SmoothStep(
                _isCritical ? CriticalFadeStart : NormalFadeStart,
                1f,
                progress);

            return settledScale * MathHelper.Lerp(1f, 0.95f, finish);
        }

        private float CalculateRotation(float progress)
        {
            // Normal hits stay axis-aligned for maximum font sharpness.
            if (!_isCritical)
                return 0f;

            // Critical hits receive only a tiny decaying tilt. Large or persistent
            // rotations make SpriteFont edges shimmer and appear serrated.
            float direction = -Math.Sign(_burstDirection.X);
            float energy = 1f - EaseOutCubic(Normalize(progress, 0f, 0.42f));
            return direction * 0.018f * energy;
        }

        private float CalculateOpacity(float progress)
        {
            float fadeStart = _isCritical ? CriticalFadeStart : NormalFadeStart;
            return 1f - SmoothStep(fadeStart, 1f, progress);
        }

        private Color CalculateColor(float progress)
        {
            // Brighten the configured color during impact without blending to white.
            // This keeps the number energetic while preserving clean colored edges.
            float impactEnergy = 1f - SmoothStep(0f, _isCritical ? 0.22f : 0.16f, progress);
            float multiplier = 1f + impactEnergy * (_isCritical ? 0.16f : 0.08f);
            return BoostColor(_sourceColor, multiplier);
        }

        private void Deactivate()
        {
            if (_recycled)
                return;

            Hidden = true;

            if (World is WorldControl worldControl)
                worldControl.RemoveObject(this);

            Recycle();
        }

        private WalkerObject ResolveTarget()
        {
            GameScene scene = MuGame.Instance?.ActiveScene as GameScene;
            if (scene == null || World == null)
                return null;

            ushort localId = MuGame.Network.GetCharacterState().Id;
            if (TargetId == localId)
                return scene.Hero;

            return World.TryGetWalkerById(TargetId, out WalkerObject target)
                ? target
                : null;
        }

        private Vector3 CalculateImpactPoint(WalkerObject target)
        {
            const int PlayerHeadBoneIndex = 20;

            if (target is PlayerObject player)
            {
                Matrix[] bones = player.GetBoneTransforms();
                if (bones != null &&
                    bones.Length > PlayerHeadBoneIndex &&
                    bones[PlayerHeadBoneIndex] != default)
                {
                    Vector3 local = bones[PlayerHeadBoneIndex].Translation;
                    Vector3 world = Vector3.Transform(local, player.WorldPosition);
                    return world + Vector3.UnitZ * PlayerHeadBoneTextOffsetZ;
                }

                return player.Position + Vector3.UnitZ * PlayerFallbackHeight;
            }

            if (target is ModelObject model)
            {
                Matrix[] bones = model.GetBoneTransforms();
                if (bones != null && bones.Length > 0)
                {
                    float highestZ = float.MinValue;

                    for (int i = 0; i < bones.Length; i++)
                    {
                        Vector3 local = bones[i].Translation;
                        Vector3 world = Vector3.Transform(local, model.WorldPosition);
                        highestZ = Math.Max(highestZ, world.Z);
                    }

                    if (highestZ > float.MinValue)
                    {
                        Vector3 modelPosition = model.WorldPosition.Translation;
                        return new Vector3(
                            modelPosition.X,
                            modelPosition.Y,
                            highestZ - MonsterTopInset);
                    }
                }
            }

            BoundingBox bounds = target.BoundingBoxWorld;
            return new Vector3(
                (bounds.Min.X + bounds.Max.X) * 0.5f,
                (bounds.Min.Y + bounds.Max.Y) * 0.5f,
                MathHelper.Lerp(bounds.Min.Z, bounds.Max.Z, 0.82f));
        }

        private static bool DetectCriticalHit(string text, Color color)
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (text.IndexOf("CRIT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf('!') >= 0 ||
                    text.StartsWith("*", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            int maximum = Math.Max(color.R, Math.Max(color.G, color.B));
            int minimum = Math.Min(color.R, Math.Min(color.G, color.B));
            bool saturated = maximum - minimum >= 100;
            bool warm = color.R >= 210 && color.G >= 115 && color.B <= 125;
            return saturated && warm;
        }

        private static void DrawText(
            SpriteBatch spriteBatch,
            SpriteFont font,
            string text,
            Vector2 position,
            Color color,
            Vector2 origin,
            float scale,
            float rotation)
        {
            spriteBatch.DrawString(
                font,
                text,
                position,
                color,
                rotation,
                origin,
                scale,
                SpriteEffects.None,
                0f);
        }

        private static Color BoostColor(Color color, float multiplier)
        {
            return new Color(
                (byte)MathHelper.Clamp(color.R * multiplier, 0f, 255f),
                (byte)MathHelper.Clamp(color.G * multiplier, 0f, 255f),
                (byte)MathHelper.Clamp(color.B * multiplier, 0f, 255f),
                color.A);
        }

        private static Color ApplyOpacity(Color color, float opacity)
        {
            // SpriteBatch with BlendState.AlphaBlend expects premultiplied color.
            // Multiplying the whole Color scales RGB and A together, preventing
            // bright fringes on anti-aliased SpriteFont edge pixels.
            return color * MathHelper.Clamp(opacity, 0f, 1f);
        }

        private static float RandomRange(float minimum, float maximum)
        {
            return MathHelper.Lerp(
                minimum,
                maximum,
                (float)MuGame.Random.NextDouble());
        }

        private static float Normalize(float value, float minimum, float maximum)
        {
            if (maximum <= minimum)
                return value >= maximum ? 1f : 0f;

            return MathHelper.Clamp((value - minimum) / (maximum - minimum), 0f, 1f);
        }

        private static float SmoothStep(float minimum, float maximum, float value)
        {
            float normalized = Normalize(value, minimum, maximum);
            return normalized * normalized * (3f - 2f * normalized);
        }

        private static float EaseOutCubic(float value)
        {
            value = MathHelper.Clamp(value, 0f, 1f);
            float inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }

        private static float EaseOutExpo(float value)
        {
            value = MathHelper.Clamp(value, 0f, 1f);
            return value >= 1f
                ? 1f
                : 1f - (float)Math.Pow(2f, -10f * value);
        }

        private static float EaseOutBack(float value)
        {
            value = MathHelper.Clamp(value, 0f, 1f);
            const float overshoot = 1.70158f;
            float shifted = value - 1f;
            return 1f + (overshoot + 1f) * shifted * shifted * shifted +
                overshoot * shifted * shifted;
        }
    }
}
