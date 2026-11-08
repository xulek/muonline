using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Effect carried by a model (item link emitters, full set auras). It is not a world object: the owner keeps
    /// it (<see cref="ModelObject.AddItemEffect"/>) and hands it to <see cref="ItemEffectSystem"/> from its update
    /// and DrawAfter, so it runs exactly when the owner is updated and drawn, without its own place in the object
    /// hierarchy, transforms or culling.
    /// </summary>
    public abstract class ItemEffect : IDisposable
    {
        /// <summary>The model carrying the effect.</summary>
        public ModelObject Owner { get; internal set; }

        protected bool IsDisposed { get; private set; }

        protected static GraphicsDevice GraphicsDevice => MuGame.Instance.GraphicsDevice;

        /// <summary>One frame of the effect's simulation.</summary>
        internal abstract void TickEffect(GameTime gameTime);

        /// <summary>Draws the effect (into the world batches while they are open).</summary>
        internal abstract void RenderEffect();

        public virtual void Dispose()
        {
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
