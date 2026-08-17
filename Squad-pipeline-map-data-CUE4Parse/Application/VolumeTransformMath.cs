using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal static class VolumeTransformMath
{
    public static double Multiply(double left, double right) => (float)left * (float)right;

    public static double ScaleSphereRadius(double radius, Vec3 scale) => Multiply(radius, scale.Z);

    public static double ScaleCapsuleRadius(double radius, Vec3 scale) => Multiply(radius, scale.Y);

    public static double ScaleCapsuleHalfHeight(double halfHeight, Vec3 scale) => Multiply(halfHeight, scale.Z);

    public static double Size(Vec3 value) => MathF.Sqrt(
        (float)(value.X * value.X + value.Y * value.Y + value.Z * value.Z));

    public static double CleanRotation(double value) => Math.Abs(value) < 0.00001 ? 0 : value;

    public static Vec3 RotateExtents(Vec3 extent, Rotator rotation)
    {
        var pitch = rotation.Pitch * Math.PI / 180;
        var yaw = rotation.Yaw * Math.PI / 180;
        var roll = rotation.Roll * Math.PI / 180;
        var cp = Math.Cos(pitch);
        var sp = Math.Sin(pitch);
        var cy = Math.Cos(yaw);
        var sy = Math.Sin(yaw);
        var cr = Math.Cos(roll);
        var sr = Math.Sin(roll);
        return new Vec3(
            (float)(Math.Abs(cy * cp) * extent.X + Math.Abs(cy * sp * sr - sy * cr) * extent.Y +
                    Math.Abs(cy * sp * cr + sy * sr) * extent.Z),
            (float)(Math.Abs(sy * cp) * extent.X + Math.Abs(sy * sp * sr + cy * cr) * extent.Y +
                    Math.Abs(sy * sp * cr - cy * sr) * extent.Z),
            (float)(Math.Abs(-sp) * extent.X + Math.Abs(cp * sr) * extent.Y + Math.Abs(cp * cr) * extent.Z));
    }

}
