using System.Runtime.CompilerServices;
using Evg = Evergine.Mathematics;
using Num = System.Numerics;

namespace Longshot.Utils;

public static class MathExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Evg.Vector3 ToEvergine(this Num.Vector3 v) => new Evg.Vector3(v.X, v.Y, v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Num.Vector3 ToNumerics(this Evg.Vector3 v) => new Num.Vector3(v.X, v.Y, v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Evg.Vector2 ToEvergine(this Num.Vector2 v) => new Evg.Vector2(v.X, v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Num.Vector2 ToNumerics(this Evg.Vector2 v) => new Num.Vector2(v.X, v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Evg.Matrix4x4 ToEvergine(this Num.Matrix4x4 m) => new Evg.Matrix4x4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        );
}