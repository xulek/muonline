#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.SkillFx
{
    /// <summary>
    /// Base for faithful SourceMain5.2 skill ports. Gameplay logic runs in <see cref="Tick"/> at the
    /// original 25 Hz (one OBJECT LifeTime unit per tick); rendering happens in <see cref="DrawFx"/>.
    /// Owns its model renderers and dynamic lights and releases them on dispose.
    /// </summary>
    public abstract class LegacySkillEffect : EffectObject
    {
        protected const float TickSeconds = 0.04f;
        private const int MaxTicksPerFrame = 4;

        private readonly List<SkillFxModelRenderer> _renderers = new();
        private readonly List<DynamicLight> _lights = new();
        private float _accumulator;
        private bool _lightsAdded;
        private bool _finished;

        /// <summary>Number of ticks run so far (0 before the first tick).</summary>
        protected int TickCount { get; private set; }

        protected WalkerObject? Owner { get; }

        protected LegacySkillEffect(WalkerObject? owner, Vector3 position, float boundsRadius = 600f)
        {
            Owner = owner;
            Position = position;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-boundsRadius, -boundsRadius, -200f),
                new Vector3(boundsRadius, boundsRadius, boundsRadius + 200f));
        }

        protected abstract void Tick();

        protected virtual void DrawFx() { }

        protected virtual Task LoadAssets() => Task.CompletedTask;

        protected bool OwnerAlive =>
            Owner != null && Owner.Status != GameControlStatus.Disposed && Owner.World != null;

        protected TerrainControl? Terrain => World?.Terrain;

        protected float GroundHeight(Vector3 p) => Terrain?.RequestTerrainHeight(p.X, p.Y) ?? p.Z;

        /// <summary>Ends the effect after the current tick.</summary>
        protected void Finish() => _finished = true;

        protected DynamicLight AddLight(Vector3 color, float radius)
        {
            var light = new DynamicLight { Owner = this, Position = Position, Color = color, Radius = radius, Intensity = 0f };
            _lights.Add(light);
            if (_lightsAdded && World?.Terrain != null)
                World.Terrain.AddDynamicLight(light);
            return light;
        }

        protected static async Task<Texture2D?> LoadTexture(string path)
        {
            return await TextureLoader.Instance.PrepareAndGetTexture(path);
        }

        protected async Task<SkillFxModelRenderer?> LoadModel(string path, bool bright, int blendMesh = -1)
        {
            if (World == null || !await BMDLoader.Instance.AssestExist(path))
                return null;

            var renderer = new SkillFxModelRenderer(path, bright, blendMesh) { World = World };
            await renderer.Load();
            if (renderer.Status != GameControlStatus.Ready)
            {
                renderer.Dispose();
                return null;
            }

            _renderers.Add(renderer);
            return renderer;
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();
            await LoadAssets();

            if (!_lightsAdded && World?.Terrain != null)
            {
                for (int i = 0; i < _lights.Count; i++)
                    World.Terrain.AddDynamicLight(_lights[i]);
                _lightsAdded = true;
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            for (int i = 0; i < _renderers.Count; i++)
                _renderers[i].Update(gameTime);

            _accumulator += MathHelper.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0f, TickSeconds * MaxTicksPerFrame);
            int steps = 0;
            while (_accumulator >= TickSeconds && steps < MaxTicksPerFrame && !_finished)
            {
                _accumulator -= TickSeconds;
                TickCount++;
                Tick();
                steps++;
            }

            if (_finished)
                RemoveSelf();
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            if (!Visible || Status != GameControlStatus.Ready || _finished)
                return;
            DrawFx();
        }

        /// <summary>Bone world position of a player caster, or the fallback.</summary>
        protected static Vector3 BonePosition(WalkerObject owner, int bone, Vector3 fallback)
        {
            if (owner is PlayerObject player && player.TryGetBoneWorldMatrix(bone, out Matrix m))
                return m.Translation;
            return fallback;
        }

        protected static void PlaySound(string path)
        {
            if (!string.IsNullOrEmpty(path))
                SoundController.Instance.PlayBuffer(path);
        }

        private void RemoveSelf()
        {
            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);
            Dispose();
        }

        public override void Dispose()
        {
            if (World?.Terrain != null)
            {
                for (int i = 0; i < _lights.Count; i++)
                    World.Terrain.RemoveDynamicLight(_lights[i]);
            }
            _lights.Clear();
            _lightsAdded = false;

            for (int i = 0; i < _renderers.Count; i++)
                _renderers[i].Dispose();
            _renderers.Clear();

            base.Dispose();
        }
    }
}
