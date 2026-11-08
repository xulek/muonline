using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// World-level system of the item effects (<see cref="ItemEffect"/>: item link emitters, full set auras).
    /// The effects are not world objects; their owning models hand them over from Update and DrawAfter (so they
    /// run for exactly the models the world updates and draws). During a world update/draw the system collects
    /// them, ticks all of them in one loop after the update and renders all of them in one loop into the shared
    /// world batches (one effect, one draw per texture and blend state). Outside a world pass (other scenes,
    /// previews) an effect runs right away.
    /// </summary>
    internal static class ItemEffectSystem
    {
        // Nesting depth of world updates/draws; the outermost one ticks and renders.
        private static int s_updateDepth;
        private static int s_drawDepth;
        private static readonly List<ItemEffect> s_updates = new(320);
        private static readonly List<ItemEffect> s_draws = new(320);

        internal static void BeginUpdate()
        {
            if (s_updateDepth++ == 0)
                s_updates.Clear();
        }

        /// <summary>Ticks the effects handed over during the world update.</summary>
        internal static void EndUpdate(GameTime gameTime)
        {
            if (--s_updateDepth > 0)
                return;
            try
            {
                for (int i = 0; i < s_updates.Count; i++)
                    s_updates[i].TickEffect(gameTime);
            }
            finally
            {
                s_updates.Clear();
            }
        }

        internal static void BeginDraw()
        {
            if (s_drawDepth++ == 0)
                s_draws.Clear();
        }

        /// <summary>Renders the effects handed over during the DrawAfter passes into the open world batches.</summary>
        internal static void EndDraw()
        {
            if (--s_drawDepth > 0)
                return;
            try
            {
                for (int i = 0; i < s_draws.Count; i++)
                    s_draws[i].RenderEffect();
            }
            finally
            {
                s_draws.Clear();
            }
        }

        /// <summary>Ticks the effect now, or with the other effects at the end of the world update.</summary>
        internal static void Update(ItemEffect effect, GameTime gameTime)
        {
            if (s_updateDepth > 0)
                s_updates.Add(effect);
            else
                effect.TickEffect(gameTime);
        }

        /// <summary>Renders the effect with the other effects after the DrawAfter passes, or now on its own.</summary>
        internal static void Draw(ItemEffect effect)
        {
            if (s_drawDepth > 0)
            {
                s_draws.Add(effect);
                return;
            }

            // Effects only write into the batches; outside a world pass they are drawn right away.
            effect.RenderEffect();
            var graphicsDevice = MuGame.Instance.GraphicsDevice;
            ItemLinkEffect.EndWorldBatch(graphicsDevice);
            FullSetAuraEffect.EndWorldBatch(graphicsDevice);
        }
    }
}
