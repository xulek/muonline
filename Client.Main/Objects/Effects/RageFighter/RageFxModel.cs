#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// BMD effect model driven entirely by its owning Rage Fighter effect (position, angle,
    /// scale, tint and fade are written by the owner every frame). It mirrors the
    /// SourceMain5.2 <c>RenderBody(RENDER_TEXTURE | RENDER_BRIGHT, ...)</c> path used by
    /// MODEL_SHOCKWAVE*, MODEL_WINDFOCE, MODEL_WOLF_HEAD_EFFECT, the punch/kick dummies,
    /// etc.: the whole body is additive and tinted by BodyLight (<see cref="WorldObject.Light"/>),
    /// with <see cref="ModelObject.BlendMeshLight"/> acting as the fade.
    /// The owner removes it with <see cref="Kill"/>; it never removes itself.
    /// </summary>
    internal sealed class RageFxModel : ModelObject
    {
        private readonly string _path;
        private readonly string? _textureOverridePath;
        private readonly float _playSpeed;

        /// <param name="path">BMD path (e.g. "Effect/shockwave01.bmd").</param>
        /// <param name="textureOverridePath">Optional texture forced on every mesh
        /// (RenderMesh(..., BITMAP_DAMAGE2) style overrides).</param>
        /// <param name="playSpeed">Legacy PlaySpeed (keys per 25 FPS frame); 0 = static pose.</param>
        /// <param name="additive">RENDER_BRIGHT (additive) when true, plain alpha-tested texture otherwise.</param>
        public RageFxModel(string path, string? textureOverridePath = null, float playSpeed = 0f, bool additive = true)
        {
            _path = path;
            _textureOverridePath = textureOverridePath;
            _playSpeed = playSpeed;

            RenderShadow = false;
            LightEnabled = !additive;
            Light = additive ? Vector3.One : Vector3.Zero;
            BlendMeshLight = 1f;
            ContinuousAnimation = playSpeed > 0f;
            HoldOnLastFrame = playSpeed > 0f;

            if (additive)
            {
                IsTransparent = true;
                AffectedByTransparency = true;
                DepthState = DepthStencilState.DepthRead;
                BlendMesh = -2;
                BlendState = Blendings.OneOneAdditive;
                BlendMeshState = Blendings.OneOneAdditive;
            }
        }

        public bool Killed { get; private set; }

        /// <summary>Current animation key (AnimationFrame in the original).</summary>
        public float AnimationFrame => (float)GetAnimationTime();

        /// <summary>Last reachable key of action 0 while holding on the last frame (0 until loaded).</summary>
        public float LastKey =>
            Model?.Actions is { Length: > 0 } actions && actions[0] != null
                ? MathF.Max(0f, actions[0].NumAnimationKeys - 2)
                : 0f;

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare(_path);
            await base.Load();

            if (Killed || Model == null)
                return;

            if (_playSpeed > 0f && Model.Actions is { Length: > 0 } actions && actions[0] != null)
            {
                // Player-style clock: keys/s = PlaySpeed * 25 (AnimationSpeed scales the BMD action speed).
                float baseSpeed = actions[0].PlaySpeed <= 0f ? 1f : actions[0].PlaySpeed;
                AnimationSpeed = 25f * _playSpeed / baseSpeed;
            }

            if (!string.IsNullOrEmpty(_textureOverridePath))
            {
                Texture2D? texture = await TextureLoader.Instance.PrepareAndGetTexture(_textureOverridePath);
                if (texture != null && Model.Meshes != null)
                {
                    for (int i = 0; i < Model.Meshes.Length; i++)
                        SetMeshTextureOverride(i, texture);
                }
            }
        }

        public bool TryGetBoneWorld(int boneIndex, Vector3 relative, out Vector3 position)
        {
            Matrix[] bones = GetBoneTransforms();
            if (Status == GameControlStatus.Ready && bones != null && boneIndex >= 0 && boneIndex < bones.Length)
            {
                position = Vector3.Transform(relative, bones[boneIndex] * WorldPosition);
                return true;
            }

            position = Position;
            return false;
        }

        public void Kill()
        {
            if (Killed)
                return;

            Killed = true;
            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }
    }
}
