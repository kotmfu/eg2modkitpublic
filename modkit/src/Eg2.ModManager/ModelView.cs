using System.Numerics;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// A small shaded preview of a model mesh: drag to turn it, wheel to zoom, double-click to reset. Flat shading, painter's
/// order, no textures; enough to see what an item looks like next to its floor plan.
/// </summary>
sealed class ModelView : Control
{
    Vector3[] _pos = [];
    (int A, int B, int C)[] _tris = [];
    Vector3 _centre;
    float _radius = 1, _yaw = 0.7f, _pitch = 0.45f, _zoom = 1;
    Point? _drag;
    public string Caption { get; set; } = "";

    public ModelView()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Input;
    }

    public void SetMesh(MeshGeometry m, string caption)
    {
        _pos = Enumerable.Range(0, m.VertexCount).Select(m.Position).ToArray();
        _tris = Enumerable.Range(0, m.Indices.Length / 3).Select(i => ((int)m.Indices[3 * i], (int)m.Indices[3 * i + 1], (int)m.Indices[3 * i + 2])).ToArray();
        var min = _pos.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = _pos.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        _centre = (min + max) / 2;
        _radius = Math.Max(0.001f, (max - min).Length() / 2);
        (_yaw, _pitch, _zoom) = (0.7f, 0.45f, 1);
        Caption = caption;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = e.Location; Capture = true; }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = null; Capture = false; }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag is not { } d) return;
        _yaw += (e.X - d.X) * 0.01f;
        _pitch = Math.Clamp(_pitch + (e.Y - d.Y) * 0.01f, -1.5f, 1.5f);
        _drag = e.Location;
        Invalidate();
    }
    protected override void OnMouseWheel(MouseEventArgs e) { _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15f : 1 / 1.15f), 0.2f, 8); Invalidate(); }
    protected override void OnDoubleClick(EventArgs e) { (_yaw, _pitch, _zoom) = (0.7f, 0.45f, 1); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (_tris.Length == 0)
        {
            TextRenderer.DrawText(g, "No model loaded.", Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        // y is up; turn round y, then tilt round x; orthographic, fitted to the smaller side
        var rot = Matrix4x4.CreateRotationY(_yaw) * Matrix4x4.CreateRotationX(_pitch);
        float scale = Math.Min(Width, Height) * 0.45f / _radius * _zoom;
        var view = _pos.Select(p => Vector3.Transform(p - _centre, rot)).ToArray();
        var light = Vector3.Normalize(new Vector3(-0.4f, 0.8f, 0.6f));
        var faces = new List<(float Depth, PointF[] Pts, float Shade)>(_tris.Length);
        foreach (var (a, b, c) in _tris)
        {
            if (a >= view.Length || b >= view.Length || c >= view.Length) continue;
            var n = Vector3.Cross(view[b] - view[a], view[c] - view[a]);
            if (n.LengthSquared() == 0) continue;
            float shade = 0.25f + 0.75f * Math.Abs(Vector3.Dot(Vector3.Normalize(n), light));
            PointF P(Vector3 v) => new(Width / 2f + v.X * scale, Height / 2f - v.Y * scale);
            faces.Add(((view[a].Z + view[b].Z + view[c].Z) / 3, new[] { P(view[a]), P(view[b]), P(view[c]) }, shade));
        }
        var tint = Theme.Gold;
        foreach (var f in faces.OrderBy(f => f.Depth))   // far first (z grows towards the viewer)
        {
            using var brush = new SolidBrush(Color.FromArgb((int)(tint.R * f.Shade), (int)(tint.G * f.Shade), (int)(tint.B * f.Shade)));
            g.FillPolygon(brush, f.Pts);
        }
        TextRenderer.DrawText(g, $"{Caption}   ·   drag to turn, wheel to zoom", Font, new Point(6, 6), Theme.Muted);
    }
}
