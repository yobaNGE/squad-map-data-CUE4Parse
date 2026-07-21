using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal static class VolumeTransformMath
{
    public static double Multiply(double left, double right) => (float)left * (float)right;

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

    public static double RotateRadius(Vec3 extent, Rotator rotation)
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
        var rowX = Math.Sqrt(
            Square(cy * cp * extent.X) +
            Square((cy * sp * sr - sy * cr) * extent.Y) +
            Square((cy * sp * cr + sy * sr) * extent.Z));
        var rowY = Math.Sqrt(
            Square(sy * cp * extent.X) +
            Square((sy * sp * sr + cy * cr) * extent.Y) +
            Square((sy * sp * cr - cy * sr) * extent.Z));
        var rowZ = Math.Sqrt(
            Square(-sp * extent.X) +
            Square(cp * sr * extent.Y) +
            Square(cp * cr * extent.Z));
        return (float)Math.Max(rowX, Math.Max(rowY, rowZ));
    }

    private static double Square(double value) => value * value;
}
