using Client.Data.ATT;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Client.Main.Controls.Terrain
{
    /// <summary>
    /// Provides methods to query terrain properties like height, flags, and lighting.
    /// </summary>
    public class TerrainPhysics
    {
        // Keep terrain queries in exactly the same vertical coordinate system as TerrainRenderer.
        // TerrainRenderer scales height-map samples by 1.5 and adds this offset per flagged vertex.
        private const float TerrainHeightScale = 1.5f;
        private const float SpecialHeight = 1200f;
        private const byte DominantTextureAlphaThreshold = 128;

        private readonly TerrainData _data;
        private readonly TerrainLightManager _lightManager;

        public TerrainPhysics(TerrainData data, TerrainLightManager lightManager)
        {
            _data = data;
            _lightManager = lightManager;
        }

        public TWFlags RequestTerrainFlag(int x, int y)
        {
            return TryGetTerrainFlag(x, y, out var flags) ? flags : default;
        }

        /// <summary>
        /// Safely reads terrain flags for a tile.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetTerrainFlag(int x, int y, out TWFlags flags)
        {
            flags = default;

            if ((uint)x >= (uint)Constants.TERRAIN_SIZE ||
                (uint)y >= (uint)Constants.TERRAIN_SIZE)
            {
                return false;
            }

            var terrainWall = _data.Attributes?.TerrainWall;
            int index = GetTerrainIndex(x, y);
            if (terrainWall == null || (uint)index >= (uint)terrainWall.Length)
                return false;

            flags = terrainWall[index];
            return true;
        }

        /// <summary>
        /// Returns true for tiles that must not receive grass.
        /// Missing or invalid terrain data is treated as blocked.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTerrainBlocked(int x, int y)
        {
            const TWFlags blockedFlags = TWFlags.NoMove | TWFlags.NoGround;
            return !TryGetTerrainFlag(x, y, out var flags) ||
                   (flags & blockedFlags) != 0;
        }

        public float RequestTerrainHeight(float xf, float yf)
        {
            if (xf < 0f || yf < 0f ||
                _data.HeightMap == null ||
                float.IsNaN(xf) || float.IsNaN(yf) ||
                float.IsInfinity(xf) || float.IsInfinity(yf))
            {
                return 0f;
            }

            float tileX = xf / Constants.TERRAIN_SCALE;
            float tileY = yf / Constants.TERRAIN_SCALE;

            int x0 = (int)MathF.Floor(tileX);
            int y0 = (int)MathF.Floor(tileY);
            float tx = tileX - x0;
            float ty = tileY - y0;

            // TerrainRenderer builds each corner independently. Reproduce the same
            // height calculation before interpolation, including TWFlags.Height.
            float h00 = GetRenderedSampleHeight(x0, y0);
            float h10 = GetRenderedSampleHeight(x0 + 1, y0);
            float h11 = GetRenderedSampleHeight(x0 + 1, y0 + 1);
            float h01 = GetRenderedSampleHeight(x0, y0 + 1);

            // TerrainRenderer splits the quad along the h00 -> h11 diagonal:
            // triangle 1: h00, h10, h11; triangle 2: h11, h01, h00.
            // Bilinear interpolation does not lie on those triangle planes and can
            // therefore return a height below the rendered terrain on uneven tiles.
            if (tx >= ty)
            {
                return (1f - tx) * h00
                     + (tx - ty) * h10
                     + ty * h11;
            }

            return (1f - ty) * h00
                 + tx * h11
                 + (ty - tx) * h01;
        }

        /// <summary>Rendered height of one height-map node (scale and TWFlags.Height applied).</summary>
        internal float GetRenderedNodeHeight(int x, int y) => _data.HeightMap == null ? 0f : GetRenderedSampleHeight(x, y);

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private float GetRenderedSampleHeight(int x, int y)
        {
            int wrappedX = x & Constants.TERRAIN_SIZE_MASK;
            int wrappedY = y & Constants.TERRAIN_SIZE_MASK;
            int index = wrappedY * Constants.TERRAIN_SIZE + wrappedX;

            if ((uint)index >= (uint)_data.HeightMap.Length)
                return 0f;

            float height = _data.HeightMap[index].R * TerrainHeightScale;
            var terrainWall = _data.Attributes?.TerrainWall;
            if (terrainWall != null &&
                (uint)index < (uint)terrainWall.Length &&
                (terrainWall[index] & TWFlags.Height) != 0)
            {
                height += SpecialHeight;
            }

            return height;
        }

        public Vector3 RequestTerrainLight(float xf, float yf, float ambientLight)
        {
            if (_data.Attributes?.TerrainWall == null
                || xf < 0 || yf < 0
                || _data.FinalLightMap == null)
                return Vector3.One;

            xf /= Constants.TERRAIN_SCALE;
            yf /= Constants.TERRAIN_SCALE;

            int xi = (int)xf, yi = (int)yf;
            float xd = xf - xi, yd = yf - yi;

            int i1 = xi + yi * Constants.TERRAIN_SIZE;
            int i2 = (xi + 1) + yi * Constants.TERRAIN_SIZE;
            int i3 = (xi + 1) + (yi + 1) * Constants.TERRAIN_SIZE;
            int i4 = xi + (yi + 1) * Constants.TERRAIN_SIZE;

            if ((uint)i1 >= (uint)_data.FinalLightMap.Length ||
                (uint)i2 >= (uint)_data.FinalLightMap.Length ||
                (uint)i3 >= (uint)_data.FinalLightMap.Length ||
                (uint)i4 >= (uint)_data.FinalLightMap.Length)
                return Vector3.Zero;

            // Avoid array allocation - calculate channels directly
            float r = MathHelper.Lerp(
                MathHelper.Lerp(GetChannel(_data.FinalLightMap[i1], 0), GetChannel(_data.FinalLightMap[i4], 0), yd),
                MathHelper.Lerp(GetChannel(_data.FinalLightMap[i2], 0), GetChannel(_data.FinalLightMap[i3], 0), yd), xd);
            
            float g = MathHelper.Lerp(
                MathHelper.Lerp(GetChannel(_data.FinalLightMap[i1], 1), GetChannel(_data.FinalLightMap[i4], 1), yd),
                MathHelper.Lerp(GetChannel(_data.FinalLightMap[i2], 1), GetChannel(_data.FinalLightMap[i3], 1), yd), xd);
            
            float b = MathHelper.Lerp(
                MathHelper.Lerp(GetChannel(_data.FinalLightMap[i1], 2), GetChannel(_data.FinalLightMap[i4], 2), yd),
                MathHelper.Lerp(GetChannel(_data.FinalLightMap[i2], 2), GetChannel(_data.FinalLightMap[i3], 2), yd), xd);

            var result = new Vector3(r, g, b)
                       + new Vector3(ambientLight * 255f)
                       + _lightManager.EvaluateDynamicLight(new Vector2(xf * Constants.TERRAIN_SCALE, yf * Constants.TERRAIN_SCALE));
            result = Vector3.Clamp(result, Vector3.Zero, new Vector3(255f));
            return result / 255f;
        }

        /// <summary>
        /// Returns the texture that visually dominates the terrain tile.
        /// Grass uses this instead of requiring alpha to be exactly 255.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetDominantTextureIndexAt(int x, int y, out byte textureIndex)
        {
            textureIndex = 0;

            if (!TryGetMappingCell(x, y, out byte layer1, out byte layer2, out byte alpha))
                return false;

            textureIndex = alpha >= DominantTextureAlphaThreshold ? layer2 : layer1;
            return true;
        }

        public byte GetBaseTextureIndexAt(int x, int y)
        {
            x = Math.Clamp(x, 0, Constants.TERRAIN_SIZE - 1);
            y = Math.Clamp(y, 0, Constants.TERRAIN_SIZE - 1);

            if (!TryGetMappingCell(x, y, out byte layer1, out byte layer2, out byte alpha))
                return 0;

            return alpha == byte.MaxValue ? layer2 : layer1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetMappingCell(
            int x,
            int y,
            out byte layer1,
            out byte layer2,
            out byte alpha)
        {
            layer1 = 0;
            layer2 = 0;
            alpha = 0;

            if ((uint)x >= (uint)Constants.TERRAIN_SIZE ||
                (uint)y >= (uint)Constants.TERRAIN_SIZE)
            {
                return false;
            }

            var mapping = _data.Mapping;
            if (mapping.Layer1 is null ||
                mapping.Layer2 is null ||
                mapping.Alpha is null)
            {
                return false;
            }

            int index = GetTerrainIndex(x, y);
            if ((uint)index >= (uint)mapping.Layer1.Length ||
                (uint)index >= (uint)mapping.Layer2.Length ||
                (uint)index >= (uint)mapping.Alpha.Length)
            {
                return false;
            }

            layer1 = mapping.Layer1[index];
            layer2 = mapping.Layer2[index];
            alpha = mapping.Alpha[index];
            return true;
        }

        private static byte GetChannel(Color c, int index)
        {
            return index switch
            {
                0 => c.R,
                1 => c.G,
                2 => c.B,
                _ => throw new ArgumentOutOfRangeException(),
            };
        }

        public static int GetTerrainIndex(float xf, float yf)
        {
            xf /= Constants.TERRAIN_SCALE;
            yf /= Constants.TERRAIN_SCALE;

            int xi = (int)xf, yi = (int)yf;
            int index = GetTerrainIndex(xi, yi);

            return index;
        }

        public static int GetTerrainIndex(int x, int y)
            => y * Constants.TERRAIN_SIZE + x;
    }
}
