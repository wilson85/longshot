using System.Numerics;
using System.Runtime.InteropServices;
using Evergine.Common.Graphics;

namespace Longshot.Gameplay.Table
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TronVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Color Color;
        public Vector2 TexCoord;

        public static readonly LayoutDescription Layout;

        static TronVertex()
        {
            Layout = new LayoutDescription()
            .Add(new ElementDescription(ElementFormat.Float3, ElementSemanticType.Position))
            .Add(new ElementDescription(ElementFormat.Float3, ElementSemanticType.Normal))
            .Add(new ElementDescription(ElementFormat.Float4, ElementSemanticType.Tangent))
            .Add(new ElementDescription(ElementFormat.UByte4Normalized, ElementSemanticType.Color))
            .Add(new ElementDescription(ElementFormat.Float2, ElementSemanticType.TexCoord))
            ;
        }

        public TronVertex(Vector3 position, Vector3 normal, Vector4 tangent, Color color, Vector2 texCoords)
        {
            this.Position = position;
            this.Normal = normal;
            this.Tangent = tangent;
            this.Color = color;
            this.TexCoord = texCoords;
        }
    }
}
