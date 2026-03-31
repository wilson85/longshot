using System.Numerics;
using LongShot.Table;
using System.Collections.Generic;

namespace LongShot.Rendering
{
    public static class MeshUtils
    {
        // Helper to create a vertex with default white color
        private static Vertex V(Vector3 p, Vector3 n) =>
            new Vertex { Position = p, Normal = n, Color = Vector4.One };

        public static void GenerateCircle(out Vertex[] v, out ushort[] i, int segments = 32)
        {
            v = new Vertex[segments + 1];
            i = new ushort[segments * 3];
            v[0] = V(Vector3.Zero, Vector3.UnitY);
            for (int s = 0; s < segments; s++)
            {
                float angle = (float)s / segments * MathF.PI * 2f;
                v[s + 1] = V(new Vector3(MathF.Cos(angle) * 0.5f, 0, MathF.Sin(angle) * 0.5f), Vector3.UnitY);
            }
            for (int s = 0; s < segments; s++)
            {
                i[s * 3] = 0;
                i[(s * 3) + 1] = (ushort)(s + 1);
                i[(s * 3) + 2] = (ushort)(s == segments - 1 ? 1 : s + 2);
            }
        }

        public static void GenerateCube(out Vertex[] v, out ushort[] i)
        {
            Vector3[] p = { new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f) };
            Vector3[] n = { -Vector3.UnitZ, Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitY };
            int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 } };
            v = new Vertex[24]; i = new ushort[36];
            for (int face = 0; face < 6; face++)
            {
                for (int vert = 0; vert < 4; vert++)
                {
                    v[(face * 4) + vert] = V(p[f[face][vert]], n[face]);
                }
                i[(face * 6) + 0] = (ushort)((face * 4) + 0); i[(face * 6) + 1] = (ushort)((face * 4) + 2); i[(face * 6) + 2] = (ushort)((face * 4) + 1);
                i[(face * 6) + 3] = (ushort)((face * 4) + 0); i[(face * 6) + 4] = (ushort)((face * 4) + 3); i[(face * 6) + 5] = (ushort)((face * 4) + 2);
            }
        }

        public static void GenerateTableRails(TableLayout layout, float width, float height, out Vertex[] v, out ushort[] ind)
        {
            var vl = new List<Vertex>();
            var il = new List<ushort>();

            void AddQuad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector3 normal)
            {
                int i = vl.Count;
                vl.Add(V(bl, normal)); vl.Add(V(br, normal)); vl.Add(V(tr, normal)); vl.Add(V(tl, normal));
                il.Add((ushort)i); il.Add((ushort)(i + 2)); il.Add((ushort)(i + 1));
                il.Add((ushort)i); il.Add((ushort)(i + 3)); il.Add((ushort)(i + 2));
            }

            foreach (var block in layout.RailBlocks)
            {
                Vector3 f0 = block.Deep1, f1 = block.Mouth1, f2 = block.Mouth2, f3 = block.Deep2;
                Vector3 b0 = f0 - (block.Normal * width), b1 = f1 - (block.Normal * width), b2 = f2 - (block.Normal * width), b3 = f3 - (block.Normal * width);
                Vector3 yOff = new Vector3(0, height, 0);
                Vector3 tf0 = f0 + yOff, tf1 = f1 + yOff, tf2 = f2 + yOff, tf3 = f3 + yOff;
                Vector3 tb0 = b0 + yOff, tb1 = b1 + yOff, tb2 = b2 + yOff, tb3 = b3 + yOff;

                Vector3 nJaw1 = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, f0 - f1));
                if (Vector3.Dot(nJaw1, block.Normal) < 0) nJaw1 = -nJaw1;
                Vector3 nJaw2 = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, f2 - f3));
                if (Vector3.Dot(nJaw2, block.Normal) < 0) nJaw2 = -nJaw2;

                AddQuad(f1, f0, tf0, tf1, nJaw1); AddQuad(f2, f1, tf1, tf2, block.Normal); AddQuad(f3, f2, tf2, tf3, nJaw2);
                AddQuad(b0, b1, tb1, tb0, -block.Normal); AddQuad(b1, b2, tb2, tb1, -block.Normal); AddQuad(b2, b3, tb3, tb2, -block.Normal);
                AddQuad(tf0, tf1, tb1, tb0, Vector3.UnitY); AddQuad(tf1, tf2, tb2, tb1, Vector3.UnitY); AddQuad(tf2, tf3, tb3, tb2, Vector3.UnitY);
                AddQuad(f0, b0, tb0, tf0, Vector3.Normalize(f1 - f0)); AddQuad(b3, f3, tf3, tb3, Vector3.Normalize(f2 - f3));
            }
            v = vl.ToArray(); ind = il.ToArray();
        }

        public static void GenerateCylinder(out Vertex[] v, out ushort[] ind, int segments = 32)
        {
            var vl = new List<Vertex>(); var il = new List<ushort>();
            vl.Add(V(new Vector3(0, 0.5f, 0), Vector3.UnitY)); vl.Add(V(new Vector3(0, -0.5f, 0), -Vector3.UnitY));

            int capTopStart = vl.Count;
            for (int s = 0; s <= segments; s++) vl.Add(V(new Vector3(MathF.Cos((float)s / segments * MathF.PI * 2f) * 0.5f, 0.5f, MathF.Sin((float)s / segments * MathF.PI * 2f) * 0.5f), Vector3.UnitY));
            int capBotStart = vl.Count;
            for (int s = 0; s <= segments; s++) vl.Add(V(new Vector3(MathF.Cos((float)s / segments * MathF.PI * 2f) * 0.5f, -0.5f, MathF.Sin((float)s / segments * MathF.PI * 2f) * 0.5f), -Vector3.UnitY));
            int sideTopStart = vl.Count;
            for (int s = 0; s <= segments; s++) { float a = (float)s / segments * MathF.PI * 2f; vl.Add(V(new Vector3(MathF.Cos(a) * 0.5f, 0.5f, MathF.Sin(a) * 0.5f), new Vector3(MathF.Cos(a), 0, MathF.Sin(a)))); }
            int sideBotStart = vl.Count;
            for (int s = 0; s <= segments; s++) { float a = (float)s / segments * MathF.PI * 2f; vl.Add(V(new Vector3(MathF.Cos(a) * 0.5f, -0.5f, MathF.Sin(a) * 0.5f), new Vector3(MathF.Cos(a), 0, MathF.Sin(a)))); }

            for (int s = 0; s < segments; s++)
            {
                il.Add(0); il.Add((ushort)(capTopStart + s + 1)); il.Add((ushort)(capTopStart + s));
                il.Add(1); il.Add((ushort)(capBotStart + s)); il.Add((ushort)(capBotStart + s + 1));
                il.Add((ushort)(sideTopStart + s)); il.Add((ushort)(sideTopStart + s + 1)); il.Add((ushort)(sideBotStart + s));
                il.Add((ushort)(sideBotStart + s)); il.Add((ushort)(sideTopStart + s + 1)); il.Add((ushort)(sideBotStart + s + 1));
            }
            v = vl.ToArray(); ind = il.ToArray();
        }

        public static void GenerateQuad(out Vertex[] v, out ushort[] i)
        {
            v = new Vertex[4] {
                V(new Vector3(-0.5f, 0, -0.5f), Vector3.UnitY), V(new Vector3( 0.5f, 0, -0.5f), Vector3.UnitY),
                V(new Vector3(-0.5f, 0,  0.5f), Vector3.UnitY), V(new Vector3( 0.5f, 0,  0.5f), Vector3.UnitY)
            };
            i = new ushort[6] { 0, 1, 2, 2, 1, 3 };
        }

        public static void GenerateSphere(out Vertex[] v, out ushort[] ind, int lat, int lon, float r)
        {
            var vl = new List<Vertex>(); var il = new List<ushort>();
            for (int y = 0; y <= lat; y++)
            {
                float phi = y / (float)lat * MathF.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float theta = x / (float)lon * MathF.PI * 2;
                    Vector3 p = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                    vl.Add(V(p * r, p));
                }
            }
            for (int y = 0; y < lat; y++)
            {
                for (int x = 0; x < lon; x++)
                {
                    int i0 = (y * (lon + 1)) + x, i1 = i0 + lon + 1;
                    il.Add((ushort)i0); il.Add((ushort)(i0 + 1)); il.Add((ushort)i1);
                    il.Add((ushort)i1); il.Add((ushort)(i0 + 1)); il.Add((ushort)(i1 + 1));
                }
            }
            v = vl.ToArray(); ind = il.ToArray();
        }
    }
}