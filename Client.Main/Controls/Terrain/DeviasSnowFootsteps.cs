using System;

namespace Client.Main.Controls.Terrain
{
    // Advance in travelled distance, not frames. Randomness is consumed only on
    // a footfall so camera motion and frame rate cannot change the gait.
    internal sealed class DeviasSnowFootsteps
    {
        // Distance (in actor-scale units) over which the print orientation follows a
        // change of travel direction. Distance-based, so it is frame-rate independent.
        private const float HeadingDistance = 24f;

        private uint _random;
        private float _remaining = 10f;
        private bool _left;
        private float _headingX, _headingY;
        private bool _hasHeading;

        public DeviasSnowFootsteps(int seed) => _random = unchecked((uint)seed) | 1u;

        public void Reset()
        {
            _remaining = 10f;
            _hasHeading = false;
        }

        public int StampMovement(DeviasSnowField field, float ax, float ay, float bx, float by,
            float scale, float depth, double now)
        {
            float dx = bx - ax, dy = by - ay;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            if (!float.IsFinite(length) || length <= 0 || !(scale > 0)) return 0;
            float fx = dx / length, fy = dy / length;
            float travelled = length / scale;

            // Orientation lags sharp turns and ignores per-frame path jitter; positions
            // along the path still follow the real segment.
            if (!_hasHeading)
            {
                _headingX = fx;
                _headingY = fy;
                _hasHeading = true;
            }
            else
            {
                float blend = 1f - MathF.Exp(-travelled / HeadingDistance);
                float hx = _headingX + (fx - _headingX) * blend;
                float hy = _headingY + (fy - _headingY) * blend;
                float hl = MathF.Sqrt(hx * hx + hy * hy);
                if (hl > 1e-3f) { _headingX = hx / hl; _headingY = hy / hl; }
                else { _headingX = fx; _headingY = fy; } // near-reversal: snap
            }
            float hxn = _headingX, hyn = _headingY;

            int steps = 0;
            while (_remaining <= travelled)
            {
                _left = !_left;
                float side = _left ? -1f : 1f;
                float spread = side * (11f + Variation() * 1.5f) * scale;
                float x = ax + fx * _remaining * scale - hyn * spread;
                float y = ay + fy * _remaining * scale + hxn * spread;
                float angle = side * 0.08f + Variation() * 0.12f;
                float sin = MathF.Sin(angle), cos = MathF.Cos(angle);
                float ux = (hxn * cos - hyn * sin) * scale;
                float uy = (hxn * sin + hyn * cos) * scale;
                float width = (8.5f + Variation() * 0.8f) * scale;
                float reach = 6.5f + Variation();
                float cut = MathF.Min(depth - 1f, depth * (0.9f + Variation() * 0.035f));
                // A narrower heel joins a wider forefoot. Their overlapping banks
                // form one asymmetric sole, with intact snow between successive steps.
                field.StampSegment(x - ux * reach, y - uy * reach, x - ux, y - uy,
                    width * 0.7f, cut * 0.94f, now);
                field.StampSegment(x - ux, y - uy, x + ux * reach, y + uy * reach,
                    width, cut, now);
                _remaining += 40f + Variation() * 3f;
                steps++;
            }
            _remaining -= travelled;
            return steps;
        }

        // xorshift32: the high bits have the best statistical quality.
        private float Variation()
        {
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;
            return (_random >> 16) / 32767.5f - 1f;
        }
    }
}
