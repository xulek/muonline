using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Per-frame bone palette texture for GPU-skinned draws (shader: BonePaletteTexture / BonePaletteBase /
    /// LoadBone). Poses are written as row segments of four float4 texels per bone; draws only select the
    /// palette base, so the constant buffer uploaded per draw stays small.
    /// At the start of the frame the poses of the visible objects are queued and uploaded in one call
    /// (<see cref="Queue"/> / <see cref="FlushQueued"/>); a pose first seen while drawing is uploaded on its own.
    /// </summary>
    internal static class GpuSkinPalette
    {
        /// <summary>Texels per row; the shader addresses texels as (t &amp; 1023, t &gt;&gt; 10).</summary>
        public const int Width = 1024;
        private const int BoneAlignment = 16;
        private const int InitialRows = 64;
        private const int MaxRows = 1024;
        private const int MaxBones = Width / 4;

        private readonly struct PoseKey : IEquatable<PoseKey>
        {
            public PoseKey(Matrix[] bones, uint version) { Bones = bones; Version = version; }
            public Matrix[] Bones { get; }
            public uint Version { get; }
            public bool Equals(PoseKey other) => ReferenceEquals(Bones, other.Bones) && Version == other.Version;
            public override bool Equals(object obj) => obj is PoseKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Bones), Version);
        }

        private readonly struct PoseSlot
        {
            public PoseSlot(int baseTexel, int boneCount) { BaseTexel = baseTexel; BoneCount = boneCount; }
            public int BaseTexel { get; }
            public int BoneCount { get; }
        }

        // Frames rotate through a few textures so a row is rarely rewritten while the GPU may still read it.
        private static readonly Texture2D[] Ring = new Texture2D[3];
        private static readonly List<(Texture2D Texture, int Frame)> Retired = new();
        private static readonly Dictionary<PoseKey, PoseSlot> Slots = new();
        // CPU copy of the current texture; queued rows are uploaded from it in one call.
        private static Vector4[] _mirror = Array.Empty<Vector4>();
        private static Texture2D _texture;
        private static int _rows;
        private static int _cursor;
        private static int _queuedFromTexel = -1;
        private static bool _textureReferenced;
        private static int _frame = int.MinValue;
        private static int _ringIndex;

        /// <summary>Changes whenever previously returned bases or the texture stop being valid.</summary>
        public static int Generation { get; private set; }

        public static Texture2D Texture => _texture;

        public static int UploadsThisFrame { get; private set; }

        /// <summary>Queues a pose for the batched upload of this frame (before any skinned draw).</summary>
        public static void Queue(GraphicsDevice gd, Matrix[] bones, uint poseVersion, int boneCount)
        {
            if (gd == null || bones == null || boneCount <= 0 || boneCount > MaxBones)
                return;

            BeginFrameIfNeeded(gd);
            if (_textureReferenced)
                return;

            var key = new PoseKey(bones, poseVersion);
            if (Slots.TryGetValue(key, out PoseSlot slot) && slot.BoneCount >= boneCount)
                return;

            int start = _cursor;
            if (!TryWrite(gd, key, bones, boneCount, out _))
                return;
            if (_queuedFromTexel < 0)
                _queuedFromTexel = start;
        }

        /// <summary>Uploads the queued poses.</summary>
        public static void FlushQueued()
        {
            if (_queuedFromTexel < 0 || _texture == null || _texture.IsDisposed)
            {
                _queuedFromTexel = -1;
                return;
            }

            int firstRow = _queuedFromTexel / Width;
            int endRow = (_cursor + Width - 1) / Width;
            int rowCount = Math.Max(1, endRow - firstRow);
            _texture.SetData(0, new Rectangle(0, firstRow, Width, rowCount), _mirror, firstRow * Width, rowCount * Width);
            UploadsThisFrame++;
            _queuedFromTexel = -1;
        }

        /// <summary>
        /// Returns the first texel of <paramref name="bones"/> (at least <paramref name="boneCount"/> bones, missing
        /// ones identity) in this frame's palette texture, uploading it when it was not queued.
        /// </summary>
        public static bool TryGetBase(GraphicsDevice gd, Matrix[] bones, uint poseVersion, int boneCount, out int baseTexel)
        {
            baseTexel = 0;
            if (gd == null || boneCount <= 0 || boneCount > MaxBones)
                return false;

            BeginFrameIfNeeded(gd);
            if (_queuedFromTexel >= 0)
                FlushQueued();

            var key = new PoseKey(bones, poseVersion);
            if (!Slots.TryGetValue(key, out PoseSlot slot) || slot.BoneCount < boneCount)
            {
                if (!TryWrite(gd, key, bones, boneCount, out slot))
                    return false;
                int texels = slot.BoneCount * 4;
                _texture.SetData(0, new Rectangle(slot.BaseTexel % Width, slot.BaseTexel / Width, texels, 1),
                    _mirror, slot.BaseTexel, texels);
                UploadsThisFrame++;
            }

            _textureReferenced = true;
            baseTexel = slot.BaseTexel;
            return true;
        }

        private static bool TryWrite(GraphicsDevice gd, PoseKey key, Matrix[] bones, int boneCount, out PoseSlot slot)
        {
            slot = default;
            int sourceBones = Math.Min(bones?.Length ?? 0, MaxBones);
            int bonesToWrite = Math.Min(MaxBones,
                (Math.Max(boneCount, sourceBones) + BoneAlignment - 1) / BoneAlignment * BoneAlignment);
            int texels = bonesToWrite * 4;

            // A palette never crosses a row.
            if ((_cursor % Width) + texels > Width)
                _cursor = (_cursor / Width + 1) * Width;
            if (_cursor / Width >= _rows && !Grow(gd))
                return false;

            int offset = _cursor;
            for (int bone = 0; bone < bonesToWrite; bone++)
            {
                int texel = offset + bone * 4;
                if (bone < sourceBones)
                {
                    Matrix m = bones[bone];
                    _mirror[texel + 0] = new Vector4(m.M11, m.M12, m.M13, m.M14);
                    _mirror[texel + 1] = new Vector4(m.M21, m.M22, m.M23, m.M24);
                    _mirror[texel + 2] = new Vector4(m.M31, m.M32, m.M33, m.M34);
                    _mirror[texel + 3] = new Vector4(m.M41, m.M42, m.M43, m.M44);
                }
                else
                {
                    _mirror[texel + 0] = Vector4.UnitX;
                    _mirror[texel + 1] = Vector4.UnitY;
                    _mirror[texel + 2] = Vector4.UnitZ;
                    _mirror[texel + 3] = Vector4.UnitW;
                }
            }

            slot = new PoseSlot(offset, bonesToWrite);
            Slots[key] = slot;
            _cursor += texels;
            return true;
        }

        private static void BeginFrameIfNeeded(GraphicsDevice gd)
        {
            int frame = MuGame.FrameIndex;
            if (frame == _frame && _texture is { IsDisposed: false })
                return;

            _frame = frame;
            _ringIndex = (_ringIndex + 1) % Ring.Length;
            Texture2D texture = Ring[_ringIndex];
            if (texture == null || texture.IsDisposed || !ReferenceEquals(texture.GraphicsDevice, gd) ||
                texture.Height < _rows)
            {
                texture?.Dispose();
                texture = new Texture2D(gd, Width, Math.Max(InitialRows, _rows), false, SurfaceFormat.Vector4);
                Ring[_ringIndex] = texture;
            }

            _texture = texture;
            _rows = texture.Height;
            EnsureMirror();
            _cursor = 0;
            _queuedFromTexel = -1;
            _textureReferenced = false;
            Slots.Clear();
            UploadsThisFrame = 0;
            Generation++;

            for (int i = Retired.Count - 1; i >= 0; i--)
            {
                if (frame - Retired[i].Frame > Ring.Length)
                {
                    Retired[i].Texture.Dispose();
                    Retired.RemoveAt(i);
                }
            }
        }

        private static bool Grow(GraphicsDevice gd)
        {
            int rows = _rows * 2;
            if (rows > MaxRows)
                return false;

            // Draws issued earlier this frame still reference the old texture: it is disposed a few frames later
            // and the poses start over in the new one. Before any draw the written rows simply move along.
            Retired.Add((_texture, _frame));
            _texture = new Texture2D(gd, Width, rows, false, SurfaceFormat.Vector4);
            Ring[_ringIndex] = _texture;
            _rows = rows;
            EnsureMirror();
            if (_textureReferenced)
            {
                _cursor = 0;
                _queuedFromTexel = -1;
                _textureReferenced = false;
                Slots.Clear();
            }
            else if (_cursor > 0)
            {
                _queuedFromTexel = 0;
            }

            Generation++;
            return true;
        }

        private static void EnsureMirror()
        {
            int required = _rows * Width;
            if (_mirror.Length < required)
                Array.Resize(ref _mirror, required);
        }
    }
}
