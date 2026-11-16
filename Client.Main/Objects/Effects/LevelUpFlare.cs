using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Legacy BITMAP_FLARE joint used by the level-up packet.
    /// The original effect is a 25 Hz tail, not a screen-space sprite: normal level-up uses
    /// subtype 0, while master level-up uses subtype 45/46 and their short secondary flashes.
    /// Subtype 47 (Recovery) shares the 45/46 motion without the secondary sprites.
    /// </summary>
    public sealed class LevelUpFlare : EffectObject
    {
        private const float SourceFrameRate = 25f;
        private const int NormalTailLimit = 20;
        private const int MasterTailLimit = 15;
        private const int MaxTransientSprites = 64;
        // Additive tail: dim it over its last frames instead of cutting it off at LifeTime 0.
        private const float FadeOutFrames = 12f;

        private const int ShinySprite = 0;
        private const int LightSprite = 1;
        private const int RedSprite = 2;
        private const int SmokeSprite = 3;

        private static readonly Random Rng = new Random();

        private readonly Vector3 _origin;
        private readonly Vector3 _angle;
        private readonly int _variant;
        private readonly int _jointIndex;
        private readonly int _tailLimit;
        private readonly float _scale;
        private readonly float _direction1;
        private readonly float _direction2;
        private readonly float _velocity;
        private readonly int _multiUse;
        private readonly Vector3[] _tails;
        private readonly TransientSprite[] _transientSprites =
            new TransientSprite[MaxTransientSprites];
        private readonly VertexPositionColorTexture[] _tailVertices;
        private readonly short[] _tailIndices;
        private readonly VertexPositionColorTexture[] _billboardVertices =
            new VertexPositionColorTexture[4];
        private readonly short[] _billboardIndices = { 0, 1, 2, 0, 2, 3 };

        private Texture2D _flareTexture;
        private Texture2D _lightTexture;
        private Texture2D _shinyTexture;
        private Texture2D _redTexture;
        private Texture2D _smokeTexture;
        private BasicEffect _effect;
        private float _lifeFrames;
        private float _maxLifeFrames;
        private float _legacyAccumulator;
        private int _tailCount;

        public bool IsAtFinalFrame => _variant == 45 && _lifeFrames <= 1f;

        public LevelUpFlare(
            Vector3 startPos,
            int variant = 0,
            int jointIndex = 0,
            Vector3 angle = default)
        {
            _origin = startPos;
            _angle = angle;
            _variant = variant;
            _jointIndex = jointIndex;
            _tailLimit = variant == 0 ? NormalTailLimit : MasterTailLimit;
            _tails = new Vector3[_tailLimit];
            _tailVertices = new VertexPositionColorTexture[Math.Max(0, _tailLimit - 1) * 8];
            _tailIndices = new short[Math.Max(0, _tailLimit - 1) * 12];

            Position = startPos;
            IsTransparent = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            LightEnabled = false;

            if (variant == 0)
            {
                _scale = 40f;
                _direction1 = Rng.Next(-250, 250);
                _direction2 = (Rng.Next(250) + 200) / 100f;
                _velocity = 40f;
                _multiUse = 0;
                _maxLifeFrames = 50f;
                Light = Vector3.One;
            }
            else
            {
                _scale = 30f;
                _multiUse = Rng.Next(10);
                _maxLifeFrames = 30f + _multiUse;
                _direction1 = 0f;
                _direction2 = 0f;
                _velocity = 0f;
                // Sub45/46: master level-up; sub47: Recovery (AT_SKILL_RECOVER) swirl.
                Light = variant == 47 ? new Vector3(0.7f, 0.7f, 1f) : new Vector3(0.2f, 0.2f, 1f);
            }

            _lifeFrames = _maxLifeFrames;
            _tails[0] = startPos;
            _tailCount = 1;
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _flareTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                "Effect/flare.jpg");
            if (_variant != 0)
            {
                _lightTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/flare01.jpg");
                _shinyTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/Shiny02.jpg");
                _redTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/flareRed.jpg");
                _smokeTexture = await TextureLoader.Instance.PrepareAndGetTexture(
                    "Effect/smoke01.jpg");
            }

            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                TextureEnabled = true,
                LightingEnabled = false,
                World = Matrix.Identity
            };
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            float dt = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                0.2f);
            _legacyAccumulator += dt * SourceFrameRate;

            while (_legacyAccumulator >= 1f && _lifeFrames > 0f)
            {
                _legacyAccumulator -= 1f;
                Position = CalculatePosition(gameTime);
                PushTail(Position);

                if (_variant == 45 || _variant == 46)
                    EmitMasterSubEffects();

                _lifeFrames -= 1f;
            }

            UpdateTransientSprites(dt * SourceFrameRate);
            if (_lifeFrames <= 0f)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || _effect == null || _flareTexture == null)
                return;

            GraphicsDevice graphicsDevice = GraphicsManager.Instance.GraphicsDevice;
            BlendState originalBlend = graphicsDevice.BlendState;
            DepthStencilState originalDepth = graphicsDevice.DepthStencilState;
            RasterizerState originalRasterizer = graphicsDevice.RasterizerState;
            SamplerState originalSampler = graphicsDevice.SamplerStates[0];

            try
            {
                graphicsDevice.BlendState = Blendings.OneOneAdditive;
                graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                graphicsDevice.RasterizerState = RasterizerState.CullNone;
                graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                _effect.World = Matrix.Identity;
                _effect.View = Camera.Instance.View;
                _effect.Projection = Camera.Instance.Projection;

                DrawTail();

                if (_variant == 45 || _variant == 46)
                    DrawMasterSubEffects();
            }
            finally
            {
                graphicsDevice.BlendState = originalBlend;
                graphicsDevice.DepthStencilState = originalDepth;
                graphicsDevice.RasterizerState = originalRasterizer;
                graphicsDevice.SamplerStates[0] = originalSampler;
            }
        }

        private Vector3 CalculatePosition(GameTime gameTime)
        {
            if (_variant == 0)
            {
                float count = (_direction1 + _lifeFrames) / 2f;
                return _origin + new Vector3(
                    MathF.Cos(count) * _velocity,
                    -MathF.Sin(count) * _velocity,
                    _direction2 * (_maxLifeFrames - _lifeFrames + 1f));
            }

            int frame = (int)(gameTime.TotalGameTime.TotalMilliseconds / 40.0);
            frame = (_jointIndex % 2 == 1 ? frame : -frame) + _jointIndex * 53731;

            float speed0 = 0.048f;
            float speed1 = 0.0613f;
            float speed2 = 0.1113f;
            Vector3 directionTemp = new Vector3(
                MathF.Sin((frame + 55555) * speed0) * MathF.Cos(frame * speed1),
                MathF.Sin((frame + 55555) * speed0) * MathF.Sin(frame * speed1),
                MathF.Cos((frame + 55555) * speed0));
            float sinAdd = MathF.Sin((frame + 11111) * speed2);
            float cosAdd = MathF.Cos((frame + 11111) * speed2);
            Vector3 direction = new Vector3(
                cosAdd * directionTemp.Y - sinAdd * directionTemp.Z,
                sinAdd * directionTemp.Y + cosAdd * directionTemp.Z,
                directionTemp.X);

            float life = _lifeFrames * 40f / 30f;
            float distance = life < 10f ? life * 7f : life + 60f;
            distance = distance / (30f + _multiUse) * 30f;
            float circle = MathHelper.Clamp(40f - life, 0f, 10f) * 15f;
            circle = MathHelper.Min(circle, 150f);

            Vector3 position = _origin + new Vector3(0f, 0f, 10f) + direction * circle;
            float targetWeight = 100f - distance;
            float targetNoiseX = 25f * MathF.Cos(
                (_jointIndex * 51231 + frame / 10) * 0.01f);
            float targetNoiseY = 25f * MathF.Cos(
                (_jointIndex * 51231 + 3711 + frame / 10) * 0.01f);
            float targetNoiseZ = 25f * MathF.Cos(
                (_jointIndex * 51231 + 7422 + frame / 10) * 0.01f);
            position.X = (distance * position.X + targetWeight * (_origin.X + targetNoiseX)) * 0.01f;
            position.Y = (distance * position.Y + targetWeight * (_origin.Y + targetNoiseY)) * 0.01f;
            position.Z = (distance * position.Z + targetWeight * (_origin.Z + targetNoiseZ)) * 0.01f;

            position.Z += 100f;
            return position;
        }

        private void PushTail(Vector3 position)
        {
            if (_tailCount < _tailLimit)
                _tailCount++;

            for (int i = _tailCount - 1; i > 0; i--)
                _tails[i] = _tails[i - 1];
            _tails[0] = position;
        }

        private void DrawTail()
        {
            if (_tailCount < 2)
                return;

            int vertexCount = 0;
            int indexCount = 0;
            int numberOfTails = _tailCount - 1;
            Matrix angleMatrix = Matrix.CreateRotationZ(_angle.Z);
            float remainingFrames = _lifeFrames - _legacyAccumulator;
            float fade = MathHelper.Clamp(remainingFrames / FadeOutFrames, 0f, 1f);
            fade *= fade; // ease out
            Color color = ToColor(Light * fade);

            for (int segment = 0; segment < _tailCount - 1; segment++)
            {
                Vector3 current = _tails[segment];
                Vector3 next = _tails[segment + 1];
                float light1 = (numberOfTails - segment) / (float)(_tailLimit - 1);
                float light2 = (numberOfTails - segment - 1) / (float)(_tailLimit - 1);

                Vector3 current0 = TransformTailOffset(current, new Vector3(-_scale * 0.5f, 0f, 0f), angleMatrix);
                Vector3 current1 = TransformTailOffset(current, new Vector3(_scale * 0.5f, 0f, 0f), angleMatrix);
                Vector3 current2 = TransformTailOffset(current, new Vector3(0f, 0f, -_scale * 0.5f), angleMatrix);
                Vector3 current3 = TransformTailOffset(current, new Vector3(0f, 0f, _scale * 0.5f), angleMatrix);
                Vector3 next0 = TransformTailOffset(next, new Vector3(-_scale * 0.5f, 0f, 0f), angleMatrix);
                Vector3 next1 = TransformTailOffset(next, new Vector3(_scale * 0.5f, 0f, 0f), angleMatrix);
                Vector3 next2 = TransformTailOffset(next, new Vector3(0f, 0f, -_scale * 0.5f), angleMatrix);
                Vector3 next3 = TransformTailOffset(next, new Vector3(0f, 0f, _scale * 0.5f), angleMatrix);

                int first = vertexCount;
                _tailVertices[vertexCount++] = new VertexPositionColorTexture(
                    current2, color, new Vector2(light1, 1f));
                _tailVertices[vertexCount++] = new VertexPositionColorTexture(
                    current3, color, new Vector2(light1, 0f));
                _tailVertices[vertexCount++] = new VertexPositionColorTexture(
                    next3, color, new Vector2(light2, 0f));
                _tailVertices[vertexCount++] = new VertexPositionColorTexture(
                    next2, color, new Vector2(light2, 1f));
                _tailVertices[vertexCount++] = new VertexPositionColorTexture(
                    current0, color, new Vector2(light1, 0f));
                _tailVertices[vertexCount++] = new VertexPositionColorTexture(
                    current1, color, new Vector2(light1, 1f));
                _tailVertices[vertexCount++] = new VertexPositionColorTexture(
                    next1, color, new Vector2(light2, 1f));
                _tailVertices[vertexCount++] = new VertexPositionColorTexture(
                    next0, color, new Vector2(light2, 0f));

                _tailIndices[indexCount++] = (short)first;
                _tailIndices[indexCount++] = (short)(first + 1);
                _tailIndices[indexCount++] = (short)(first + 2);
                _tailIndices[indexCount++] = (short)first;
                _tailIndices[indexCount++] = (short)(first + 2);
                _tailIndices[indexCount++] = (short)(first + 3);
                _tailIndices[indexCount++] = (short)(first + 4);
                _tailIndices[indexCount++] = (short)(first + 5);
                _tailIndices[indexCount++] = (short)(first + 6);
                _tailIndices[indexCount++] = (short)(first + 4);
                _tailIndices[indexCount++] = (short)(first + 6);
                _tailIndices[indexCount++] = (short)(first + 7);
            }

            _effect.Texture = _flareTexture;
            _effect.DiffuseColor = Vector3.One;
            _effect.Alpha = 1f;
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _tailVertices,
                    0,
                    vertexCount,
                    _tailIndices,
                    0,
                    indexCount / 3);
            }
        }

        private static Vector3 TransformTailOffset(Vector3 center, Vector3 offset, Matrix angleMatrix) =>
            center + Vector3.TransformNormal(offset, angleMatrix);

        private void EmitMasterSubEffects()
        {
            AddTransientSprite(
                ShinySprite,
                Position,
                0.85f,
                new Vector3(0.5f, 0.5f, 1f),
                8f);
            AddTransientSprite(
                LightSprite,
                Position,
                1.53f,
                new Vector3(0.5f, 0.5f, 1f),
                8f);
            AddTransientSprite(
                LightSprite,
                Position,
                1.53f,
                new Vector3(0.5f, 0.5f, 1f),
                8f);

            if (_variant == 45)
            {
                AddTransientSprite(RedSprite, Position, 0.3f, Vector3.One, 6f);
            }
            else
            {
                AddTransientSprite(
                    SmokeSprite,
                    Position,
                    0.8f,
                    new Vector3(0.4f, 1f, 0.4f),
                    20f);
            }
        }

        private void AddTransientSprite(
            int kind,
            Vector3 position,
            float scale,
            Vector3 color,
            float lifeFrames)
        {
            int slot = 0;
            float smallestLife = float.MaxValue;
            for (int i = 0; i < _transientSprites.Length; i++)
            {
                if (!_transientSprites[i].Active)
                {
                    slot = i;
                    smallestLife = 0f;
                    break;
                }

                if (_transientSprites[i].LifeFrames < smallestLife)
                {
                    smallestLife = _transientSprites[i].LifeFrames;
                    slot = i;
                }
            }

            _transientSprites[slot] = new TransientSprite
            {
                Active = true,
                Kind = kind,
                Position = position,
                Scale = scale,
                Color = color,
                LifeFrames = lifeFrames,
                MaxLifeFrames = lifeFrames
            };
        }

        private void UpdateTransientSprites(float frameDelta)
        {
            for (int i = 0; i < _transientSprites.Length; i++)
            {
                if (!_transientSprites[i].Active)
                    continue;

                _transientSprites[i].LifeFrames -= frameDelta;
                if (_transientSprites[i].LifeFrames <= 0f)
                    _transientSprites[i].Active = false;
            }
        }

        private void DrawMasterSubEffects()
        {
            for (int i = 0; i < _transientSprites.Length; i++)
            {
                ref TransientSprite sprite = ref _transientSprites[i];
                if (!sprite.Active)
                    continue;

                Texture2D texture = sprite.Kind switch
                {
                    ShinySprite => _shinyTexture,
                    LightSprite => _lightTexture,
                    RedSprite => _redTexture,
                    SmokeSprite => _smokeTexture,
                    _ => null
                };
                if (texture == null)
                    continue;

                float alpha = MathHelper.Clamp(
                    sprite.LifeFrames / sprite.MaxLifeFrames,
                    0f,
                    1f);
                DrawBillboard(texture, sprite.Position, sprite.Scale * 15f, sprite.Color, alpha);
            }
        }

        private void DrawBillboard(
            Texture2D texture,
            Vector3 position,
            float halfSize,
            Vector3 color,
            float alpha)
        {
            Vector3 right = Camera.Instance.Right;
            Vector3 up = Camera.Instance.Up;
            Vector3 rightOffset = right * halfSize;
            Vector3 upOffset = up * halfSize;
            Color tint = ToColor(color * alpha);

            _billboardVertices[0] = new VertexPositionColorTexture(
                position - rightOffset - upOffset, tint, new Vector2(0f, 1f));
            _billboardVertices[1] = new VertexPositionColorTexture(
                position + rightOffset - upOffset, tint, new Vector2(1f, 1f));
            _billboardVertices[2] = new VertexPositionColorTexture(
                position + rightOffset + upOffset, tint, new Vector2(1f, 0f));
            _billboardVertices[3] = new VertexPositionColorTexture(
                position - rightOffset + upOffset, tint, new Vector2(0f, 0f));

            _effect.Texture = texture;
            _effect.DiffuseColor = Vector3.One;
            _effect.Alpha = 1f;
            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _billboardVertices,
                    0,
                    4,
                    _billboardIndices,
                    0,
                    2);
            }
        }

        private static Color ToColor(Vector3 color) => new(
            MathHelper.Clamp(color.X, 0f, 1f),
            MathHelper.Clamp(color.Y, 0f, 1f),
            MathHelper.Clamp(color.Z, 0f, 1f),
            1f);

        public override void Dispose()
        {
            _effect?.Dispose();
            _effect = null;
            base.Dispose();
        }

        private struct TransientSprite
        {
            public bool Active;
            public int Kind;
            public Vector3 Position;
            public Vector3 Color;
            public float Scale;
            public float LifeFrames;
            public float MaxLifeFrames;
        }
    }
}
