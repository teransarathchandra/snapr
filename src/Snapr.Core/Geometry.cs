namespace Snapr.Core;

public readonly record struct Pt(double X, double Y)
{
    public Pt Offset(double dx, double dy) => new(X + dx, Y + dy);
    public double DistanceTo(Pt o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
}

public readonly record struct Rct(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool IsEmpty => W <= 0 || H <= 0;
    public Pt Center => new(X + W / 2, Y + H / 2);

    public static Rct FromPoints(Pt a, Pt b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public bool Contains(Pt p, double tolerance = 0) =>
        p.X >= X - tolerance && p.X <= Right + tolerance && p.Y >= Y - tolerance && p.Y <= Bottom + tolerance;

    public Rct Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
    public Rct Inflate(double d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);

    public Rct Intersect(Rct o)
    {
        var x = Math.Max(X, o.X);
        var y = Math.Max(Y, o.Y);
        var r = Math.Min(Right, o.Right);
        var b = Math.Min(Bottom, o.Bottom);
        return r <= x || b <= y ? default : new(x, y, r - x, b - y);
    }

    public Rct Union(Rct o)
    {
        if (IsEmpty) return o;
        if (o.IsEmpty) return this;
        var x = Math.Min(X, o.X);
        var y = Math.Min(Y, o.Y);
        return new(x, y, Math.Max(Right, o.Right) - x, Math.Max(Bottom, o.Bottom) - y);
    }
}
