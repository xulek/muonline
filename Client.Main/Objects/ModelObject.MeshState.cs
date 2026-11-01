using Client.Data.Texture;
using Client.Main.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects
{
    public abstract partial class ModelObject
    {
        [System.Flags]
        public enum MeshDirtyFlags : uint
        {
            None = 0,
            Animation = 1u << 0,
            Lighting = 1u << 1,
            Transform = 1u << 2,
            Material = 1u << 3,
            Texture = 1u << 4,
            All = uint.MaxValue
        }

        private sealed class MeshRuntimeState
        {
            public DynamicVertexBuffer CpuVertexBuffer;
            public DynamicIndexBuffer CpuIndexBuffer;

            public VertexBuffer GpuVertexBuffer;
            public IndexBuffer GpuIndexBuffer;
            public int GpuBoneCount;
            public bool GpuSkinEnabled;
            public int LastGpuCacheTouchFrame;

            public Texture2D Texture;
            public Texture2D TextureOverride;
            public TextureScript Script;
            public TextureData Data;
            public string TexturePath;
            /// <summary>Normalized texture cache key of <see cref="TexturePath"/>.</summary>
            public string TextureKey;
            /// <summary>Model whose texture path map has no entry for this mesh.</summary>
            public Client.Data.BMD.BMD TexturePathMissingFor;
            public int TexturePathMissingFrame;
            public int TexturePrepareRequestFrame;

            public bool IsRgba;
            public bool HiddenByScript;
            public bool BlendByScript;

            public MeshBufferCache BufferCache;
        }

    }
}
