using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SysWatch.UI;

// Minimal SVG path-data parser (M L H V C S Q T A Z, absolute and relative) into a GraphicsPath, enough for
// single-path icon sets such as Simple Icons. Arcs are converted to cubic Béziers.
internal static partial class SvgPath
{
    [GeneratedRegex(@"[MmLlHhVvCcSsQqTtAaZz]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex Tokens();

    public static GraphicsPath Parse(string d)
    {
        var path = new GraphicsPath(FillMode.Alternate);
        var tokens = Tokens().Matches(d).Select(m => m.Value).ToList();
        var i = 0;
        char cmd = 'M';
        PointF cur = PointF.Empty, start = PointF.Empty, lastCtrl = PointF.Empty;
        char lastCmd = ' ';
        var figureOpen = false;

        bool IsCmd(int k) => k < tokens.Count && char.IsLetter(tokens[k][0]);
        float Num() => float.Parse(tokens[i++], CultureInfo.InvariantCulture);

        // Arc flags may be written without separators ("a1 1 0 00.5 1"): split a flag token into single digits.
        float Flag()
        {
            var t = tokens[i];
            if (t.Length > 1 && (t[0] == '0' || t[0] == '1'))
            {
                tokens[i] = t[1..];
                tokens.Insert(i, t[0].ToString());
            }
            return Num();
        }

        while (i < tokens.Count)
        {
            if (IsCmd(i)) cmd = tokens[i++][0];
            var rel = char.IsLower(cmd);
            var c = char.ToUpperInvariant(cmd);
            PointF P(float x, float y) => rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);

            switch (c)
            {
                case 'M':
                    if (figureOpen) path.CloseFigure();
                    path.StartFigure();
                    cur = P(Num(), Num());
                    start = cur;
                    figureOpen = true;
                    cmd = rel ? 'l' : 'L';
                    break;
                case 'L':
                {
                    var p = P(Num(), Num());
                    path.AddLine(cur, p);
                    cur = p;
                    break;
                }
                case 'H':
                {
                    var x = Num();
                    var p = new PointF(rel ? cur.X + x : x, cur.Y);
                    path.AddLine(cur, p);
                    cur = p;
                    break;
                }
                case 'V':
                {
                    var y = Num();
                    var p = new PointF(cur.X, rel ? cur.Y + y : y);
                    path.AddLine(cur, p);
                    cur = p;
                    break;
                }
                case 'C':
                {
                    var c1 = P(Num(), Num());
                    var c2 = P(Num(), Num());
                    var p = P(Num(), Num());
                    path.AddBezier(cur, c1, c2, p);
                    lastCtrl = c2;
                    cur = p;
                    break;
                }
                case 'S':
                {
                    var c1 = "CcSs".Contains(lastCmd) ? new PointF(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y) : cur;
                    var c2 = P(Num(), Num());
                    var p = P(Num(), Num());
                    path.AddBezier(cur, c1, c2, p);
                    lastCtrl = c2;
                    cur = p;
                    break;
                }
                case 'Q':
                {
                    var q = P(Num(), Num());
                    var p = P(Num(), Num());
                    AddQuad(path, cur, q, p);
                    lastCtrl = q;
                    cur = p;
                    break;
                }
                case 'T':
                {
                    var q = "QqTt".Contains(lastCmd) ? new PointF(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y) : cur;
                    var p = P(Num(), Num());
                    AddQuad(path, cur, q, p);
                    lastCtrl = q;
                    cur = p;
                    break;
                }
                case 'A':
                {
                    var rx = Num();
                    var ry = Num();
                    var rot = Num();
                    var large = Flag() != 0;
                    var sweep = Flag() != 0;
                    var p = P(Num(), Num());
                    AddArc(path, cur, rx, ry, rot, large, sweep, p);
                    cur = p;
                    break;
                }
                case 'Z':
                    path.CloseFigure();
                    figureOpen = false;
                    cur = start;
                    break;
                default:
                    i++;
                    break;
            }
            lastCmd = cmd;
        }
        return path;
    }

    private static void AddQuad(GraphicsPath path, PointF p0, PointF q, PointF p1) =>
        path.AddBezier(p0,
            new PointF(p0.X + 2f / 3 * (q.X - p0.X), p0.Y + 2f / 3 * (q.Y - p0.Y)),
            new PointF(p1.X + 2f / 3 * (q.X - p1.X), p1.Y + 2f / 3 * (q.Y - p1.Y)), p1);

    // SVG endpoint arc → centre parameterisation → cubic Bézier segments of at most 90°.
    private static void AddArc(GraphicsPath path, PointF p0, float rx, float ry, float rotDeg, bool large, bool sweep, PointF p1)
    {
        if (rx == 0 || ry == 0 || p0 == p1)
        {
            path.AddLine(p0, p1);
            return;
        }
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        var phi = rotDeg * Math.PI / 180;
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        var dx = (p0.X - p1.X) / 2.0;
        var dy = (p0.Y - p1.Y) / 2.0;
        var x1 = cos * dx + sin * dy;
        var y1 = -sin * dx + cos * dy;
        var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        double rxd = rx, ryd = ry;
        if (lambda > 1)
        {
            rxd *= Math.Sqrt(lambda);
            ryd *= Math.Sqrt(lambda);
        }
        var num = rxd * rxd * ryd * ryd - rxd * rxd * y1 * y1 - ryd * ryd * x1 * x1;
        var den = rxd * rxd * y1 * y1 + ryd * ryd * x1 * x1;
        var coef = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, num / den));
        var cx1 = coef * rxd * y1 / ryd;
        var cy1 = coef * -ryd * x1 / rxd;
        var cx = cos * cx1 - sin * cy1 + (p0.X + p1.X) / 2.0;
        var cy = sin * cx1 + cos * cy1 + (p0.Y + p1.Y) / 2.0;
        static double Angle(double ux, double uy, double vx, double vy)
        {
            var a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            return a;
        }
        var theta1 = Angle(1, 0, (x1 - cx1) / rxd, (y1 - cy1) / ryd);
        var delta = Angle((x1 - cx1) / rxd, (y1 - cy1) / ryd, (-x1 - cx1) / rxd, (-y1 - cy1) / ryd);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;

        var segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2));
        var step = delta / segments;
        var t = 4.0 / 3 * Math.Tan(step / 4);
        var prev = p0;
        for (var s = 0; s < segments; s++)
        {
            var a1 = theta1 + s * step;
            var a2 = a1 + step;
            double c1x = Math.Cos(a1), s1y = Math.Sin(a1), c2x = Math.Cos(a2), s2y = Math.Sin(a2);
            PointF Map(double ex, double ey) => new((float)(cx + rxd * ex * cos - ryd * ey * sin), (float)(cy + rxd * ex * sin + ryd * ey * cos));
            var q1 = Map(c1x - t * s1y, s1y + t * c1x);
            var q2 = Map(c2x + t * s2y, s2y - t * c2x);
            var end = s == segments - 1 ? p1 : Map(c2x, s2y);
            path.AddBezier(prev, q1, q2, end);
            prev = end;
        }
    }
}
