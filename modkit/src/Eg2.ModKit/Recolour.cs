namespace Eg2.ModKit;

/// <summary>
/// Moves one colour range of a texture to another while keeping its shading: pixels whose hue is within
/// <see cref="Half"/> degrees of <see cref="Hue"/> and whose saturation is at least <see cref="MinSat"/> get hue
/// <see cref="ToHue"/> (null = keep) with saturation and value scaled. Edges fade over 10° / 0.1 saturation, so
/// neighbouring colours (skin next to an orange sleeve) blend instead of cutting off.
/// </summary>
public record HueRule(float Hue, float Half, float MinSat, float? ToHue, float SatMul, float ValMul);

public static class Recolour
{
    public static void Apply(byte[] rgba, IReadOnlyList<HueRule> rules)
    {
        for (int i = 0; i < rgba.Length; i += 4)
        {
            var (h, s, v) = ToHsv(rgba[i], rgba[i + 1], rgba[i + 2]);
            // rules claim the pixel in order; what a rule's soft edge leaves over goes to the next one
            float left = 1, outR = 0, outG = 0, outB = 0;
            foreach (var r in rules)
            {
                float dh = Math.Abs(((h - r.Hue) % 360 + 540) % 360 - 180);
                float wHue = Math.Clamp((r.Half + 10 - dh) / 10, 0, 1), wSat = Math.Clamp((s - r.MinSat + 0.1f) / 0.1f, 0, 1);
                float w = wHue * wSat * left;
                if (w <= 0) continue;
                float nh = r.ToHue ?? h, ns = Math.Clamp(s * r.SatMul, 0, 1), nv = Math.Clamp(v * r.ValMul, 0, 1);
                var (nr, ng, nb) = FromHsv(nh, ns, nv);
                outR += nr * w; outG += ng * w; outB += nb * w;
                left -= w;
                if (left <= 0) break;
            }
            if (left >= 1) continue;
            rgba[i] = Mix(rgba[i], outR / (1 - left), 1 - left);
            rgba[i + 1] = Mix(rgba[i + 1], outG / (1 - left), 1 - left);
            rgba[i + 2] = Mix(rgba[i + 2], outB / (1 - left), 1 - left);
        }
    }

    static byte Mix(byte a, float b, float w) => (byte)Math.Clamp((int)Math.Round(a + (b * 255 - a) * w), 0, 255);

    public static (float H, float S, float V) ToHsv(byte rb, byte gb, byte bb)
    {
        float r = rb / 255f, g = gb / 255f, b = bb / 255f;
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        float h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d + 6) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static (float R, float G, float B) FromHsv(float h, float s, float v)
    {
        float c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = (int)(((h % 360) + 360) % 360 / 60) switch
        {
            0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x), 3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x),
        };
        return (r + m, g + m, b + m);
    }
}
