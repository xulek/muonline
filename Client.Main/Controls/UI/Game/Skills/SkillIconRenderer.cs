using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Skills
{
    /// <summary>
    /// Desenho compartilhado dos ícones redondos de skill (remaster oval): recorte circular
    /// por scanline — OZJ é JPEG sem alpha, então desenhar a célula quadrada mostraria os
    /// cantos pretos. Usado pelo cluster de toque e pelo painel de skills.
    /// </summary>
    internal static class SkillIconRenderer
    {
        /// <summary>
        /// Desenha a região quadrada <paramref name="src"/> da textura preenchendo o círculo
        /// inscrito em <paramref name="destRect"/>, uma faixa horizontal por linha (recorte
        /// circular real, sem shader/alpha). <paramref name="inset"/> encolhe o raio (px)
        /// pra encaixar dentro de um aro.
        /// </summary>
        public static void DrawCircleClipped(SpriteBatch sb, Texture2D tex, Rectangle src, Rectangle destRect, Color color, float inset = 2f)
        {
            float r = destRect.Width / 2f - inset;
            if (r <= 1f) return;
            var c = new Vector2(destRect.X + destRect.Width / 2f, destRect.Y + destRect.Height / 2f);
            float srcScale = src.Width / (r * 2f); // texels por pixel de destino

            int yTop = (int)MathF.Ceiling(c.Y - r);
            int yBot = (int)MathF.Floor(c.Y + r);
            for (int y = yTop; y <= yBot; y++)
            {
                float dy = y + 0.5f - c.Y;
                float t = 1f - (dy * dy) / (r * r);
                if (t <= 0f) continue;
                float hw = r * MathF.Sqrt(t);
                int x0 = (int)MathF.Round(c.X - hw);
                int x1 = (int)MathF.Round(c.X + hw);
                if (x1 <= x0) continue;

                int sy = src.Y + (int)((dy + r) * srcScale);
                int sh = Math.Max(1, (int)MathF.Ceiling(srcScale));
                int sx = src.X + (int)((x0 - (c.X - r)) * srcScale);
                int sw = Math.Max(1, (int)((x1 - x0) * srcScale));
                // Clamp dentro da célula (bordas por arredondamento).
                sy = Math.Min(sy, src.Y + src.Height - 1);
                sh = Math.Min(sh, src.Y + src.Height - sy);
                sx = Math.Min(sx, src.X + src.Width - 1);
                sw = Math.Min(sw, src.X + src.Width - sx);

                sb.Draw(tex, new Rectangle(x0, y, x1 - x0, 1), new Rectangle(sx, sy, sw, sh), color);
            }
        }

        private static readonly Dictionary<int, Texture2D> _circleCache = new();
        private static readonly HashSet<int> _circleFailed = new();

        /// <summary>
        /// Mapeia o atlas colorido pro atlas "apagado" (skills não aprendidas):
        /// newui_skill*.OZJ -> newui_non_skill*.OZJ (par dessaturado, mesmo layout 1K).
        /// O atlas de pet-commands não tem par "non" — mantém o colorido.
        /// </summary>
        public static string DisabledVariant(string path) =>
            path.Replace("newui_skill", "newui_non_skill");

        /// <summary>
        /// Ícone circular pré-cortado como textura própria (uma vez por skill): recorta o
        /// miolo redondo da célula do atlas em CPU com borda anti-aliased e alpha
        /// PREMULTIPLICADO. Desenhar como quad único fica nítido sob LinearClamp — o clip
        /// por scanline (faixas de 1px com src truncado por linha) borra/mancha com
        /// filtragem bilinear. Lê os pixels DECODIFICADOS do TextureLoader (CPU): NUNCA
        /// usar Texture2D.GetData aqui — readback de GPU trava o cliente Android
        /// (GLES2/ANGLE) dentro do Draw. Retorna null enquanto o atlas não carregou
        /// (caller usa o fallback por scanline). Chamar só na thread de draw (cria textura).
        /// </summary>
        public static Texture2D GetCircleIconTexture(ushort skillId, bool disabled = false)
        {
            int key = skillId | (disabled ? 1 << 16 : 0);
            if (_circleCache.TryGetValue(key, out var cached))
                return cached;
            if (_circleFailed.Contains(key))
                return null;

            var definition = Core.Utilities.SkillDatabase.GetSkillDefinition(skillId);
            if (!SkillIconAtlas.TryResolve(skillId, definition, out var frame))
            {
                _circleFailed.Add(key);
                return null;
            }

            string texPath = disabled ? DisabledVariant(frame.TexturePath) : frame.TexturePath;
            var info = Content.TextureLoader.Instance.Get(texPath);
            if (info?.Data == null || info.Data.Length == 0 || info.Width <= 0)
                return null; // atlas ainda carregando — tenta de novo no próximo draw
            if (info.IsCompressed || (info.Components != 3 && info.Components != 4))
            {
                _circleFailed.Add(key); // formato inesperado: fica no fallback scanline
                return null;
            }

            try
            {
                var src256 = SkillIconAtlas.CircleSource(frame.SourceRectangle);
                int sx = src256.X * info.Width / SkillIconAtlas.AtlasSize;
                int sy = src256.Y * info.Height / SkillIconAtlas.AtlasSize;
                int w = src256.Width * info.Width / SkillIconAtlas.AtlasSize;
                int h = src256.Height * info.Height / SkillIconAtlas.AtlasSize;
                int comps = info.Components;

                var data = new Color[w * h];
                float cx = (w - 1) / 2f, cy = (h - 1) / 2f;
                float r = MathF.Min(w, h) / 2f - 0.5f;
                const float edge = 1.5f; // rampa anti-aliased na borda
                int solidCount = 0, circleCount = 0;
                for (int y = 0; y < h; y++)
                {
                    int rowBase = ((sy + y) * info.Width + sx) * comps;
                    for (int x = 0; x < w; x++)
                    {
                        int o = rowBase + x * comps;
                        float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        float a = Math.Clamp((r - d) / edge, 0f, 1f);
                        // Atlas OZP tem alpha (premultiplicado) — respeitar, senão célula
                        // vazia (transparente) vira círculo PRETO opaco. OZJ (3 comps) = opaco.
                        int srcA = comps == 4 ? info.Data[o + 3] : 255;
                        if (a > 0.5f)
                        {
                            circleCount++;
                            if (srcA > 64) solidCount++;
                        }
                        data[y * w + x] = new Color(
                            (byte)(info.Data[o] * a),
                            (byte)(info.Data[o + 1] * a),
                            (byte)(info.Data[o + 2] * a),
                            (byte)(srcA * a));
                    }
                }

                if (circleCount == 0 || solidCount < circleCount * 3 / 100)
                {
                    // Célula vazia ou fantasma (só resíduo translúcido): não há ícone
                    // usável — cachear a falha pros callers desenharem o soquete no lugar.
                    _circleFailed.Add(key);
                    return null;
                }

                var cut = new Texture2D(MuGame.Instance.GraphicsDevice, w, h);
                cut.SetData(data);
                _circleCache[key] = cut;
                return cut;
            }
            catch
            {
                _circleFailed.Add(key);
                return null;
            }
        }

        /// <summary>
        /// Resolve e desenha o ícone circular de uma skill dentro de destRect (recorte do
        /// miolo 20×20 da célula do atlas + clip circular). Retorna false se atlas/textura
        /// indisponíveis (caller desenha fallback).
        /// </summary>
        public static bool DrawSkillCircle(SpriteBatch sb, ushort skillId, Rectangle destRect, Color color, float inset = 2f)
        {
            var definition = Core.Utilities.SkillDatabase.GetSkillDefinition(skillId);
            if (!SkillIconAtlas.TryResolve(skillId, definition, out var frame))
                return false;
            var tex = Content.TextureLoader.Instance.GetTexture2D(frame.TexturePath);
            if (tex == null)
                return false;
            var src = SkillIconAtlas.ScaleToTexture(SkillIconAtlas.CircleSource(frame.SourceRectangle), tex);
            DrawCircleClipped(sb, tex, src, destRect, color, inset);
            return true;
        }
    }
}
