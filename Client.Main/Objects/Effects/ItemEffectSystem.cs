using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// World-level driver of the per-item effects: item link emitters (<see cref="ItemLinkEffect"/>) and full set
    /// auras (<see cref="FullSetAuraEffect"/>). While a world updates and draws, the effects reached by its object
    /// hierarchy (so the same visible set as before) only register here; the system then ticks all of them in one
    /// loop after the world update and renders all of them in one loop into the shared world batches (one effect,
    /// one draw per texture and blend state). Outside a world pass the effects run on their own.
    /// </summary>
    internal static class ItemEffectSystem
    {
        // Nesting depth of world updates/draws; the outermost one ticks and renders.
        private static int s_updateDepth;
        private static int s_drawDepth;
        private static bool CollectingUpdates => s_updateDepth > 0;
        private static bool CollectingDraws => s_drawDepth > 0;
        private static readonly List<ItemLinkEffect> s_linkUpdates = new(256);
        private static readonly List<ItemLinkEffect> s_linkDraws = new(256);
        private static readonly List<FullSetAuraEffect> s_auraUpdates = new(64);
        private static readonly List<FullSetAuraEffect> s_auraDraws = new(64);

        internal static void BeginUpdate()
        {
            if (s_updateDepth++ > 0)
                return;
            s_linkUpdates.Clear();
            s_auraUpdates.Clear();
        }

        /// <summary>Ticks the effects registered during the world update.</summary>
        internal static void EndUpdate(GameTime gameTime)
        {
            if (--s_updateDepth > 0)
                return;
            try
            {
                for (int i = 0; i < s_linkUpdates.Count; i++)
                    s_linkUpdates[i].TickEffect(gameTime);
                for (int i = 0; i < s_auraUpdates.Count; i++)
                    s_auraUpdates[i].TickEffect(gameTime);
            }
            finally
            {
                s_linkUpdates.Clear();
                s_auraUpdates.Clear();
            }
        }

        internal static void BeginDraw()
        {
            if (s_drawDepth++ > 0)
                return;
            s_linkDraws.Clear();
            s_auraDraws.Clear();
        }

        /// <summary>Renders the effects registered during the DrawAfter passes into the open world batches.</summary>
        internal static void EndDraw()
        {
            if (--s_drawDepth > 0)
                return;
            try
            {
                for (int i = 0; i < s_linkDraws.Count; i++)
                    s_linkDraws[i].RenderEffect();
                for (int i = 0; i < s_auraDraws.Count; i++)
                    s_auraDraws[i].RenderEffect();
            }
            finally
            {
                s_linkDraws.Clear();
                s_auraDraws.Clear();
            }
        }

        internal static bool TryDeferUpdate(ItemLinkEffect effect)
        {
            if (!CollectingUpdates)
                return false;
            s_linkUpdates.Add(effect);
            return true;
        }

        internal static bool TryDeferUpdate(FullSetAuraEffect effect)
        {
            if (!CollectingUpdates)
                return false;
            s_auraUpdates.Add(effect);
            return true;
        }

        internal static bool TryDeferDraw(ItemLinkEffect effect)
        {
            if (!CollectingDraws)
                return false;
            s_linkDraws.Add(effect);
            return true;
        }

        internal static bool TryDeferDraw(FullSetAuraEffect effect)
        {
            if (!CollectingDraws)
                return false;
            s_auraDraws.Add(effect);
            return true;
        }
    }
}
