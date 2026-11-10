using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Worlds
{
    /// <summary>
    /// World shown behind a menu (login, character selection): drawn into its own target between Draw and DrawAfter,
    /// then written back with glow, colour grade and vignette (<see cref="Graphics.SceneBloom"/>). Off with
    /// <see cref="Constants.ENABLE_MENU_SCENE_EFFECTS"/>.
    /// </summary>
    public abstract class CinematicWorld : WorldControl
    {
        private bool _glowActive;

        protected CinematicWorld(short worldIndex) : base(worldIndex)
        {
        }

        /// <summary>Glow and grade settings of this scene.</summary>
        internal SceneBloom Glow { get; } = new();

        public override void Draw(GameTime gameTime)
        {
            var gd = GraphicsManager.Instance.GraphicsDevice;
            _glowActive = Constants.ENABLE_MENU_SCENE_EFFECTS && Status == GameControlStatus.Ready && Glow.Begin(gd, ClearColor);
            base.Draw(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            if (_glowActive)
            {
                _glowActive = false;
                Glow.End(GraphicsManager.Instance.GraphicsDevice);
            }
        }

        public override void Dispose()
        {
            Glow.Dispose();
            base.Dispose();
        }
    }
}
