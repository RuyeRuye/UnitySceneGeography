using System;

namespace SceneGeography
{
    internal readonly struct Matrix3d
    {
        internal readonly GeoVector3d X, Y, Z;
        internal Matrix3d(GeoVector3d x, GeoVector3d y, GeoVector3d z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal static Matrix3d Identity => new Matrix3d(GeoVector3d.UnitX, GeoVector3d.UnitY, GeoVector3d.UnitZ);

        internal GeoVector3d Apply(GeoVector3d p) => X * p.X + Y * p.Y + Z * p.Z;
        internal GeoVector3d Inverse(GeoVector3d p) => new GeoVector3d(GeoVector3d.Dot(X, p), GeoVector3d.Dot(Y, p), GeoVector3d.Dot(Z, p));
        internal static Matrix3d FromQuaternion(double w, double x, double y, double z)
        {
            double n = Math.Sqrt(w * w + x * x + y * y + z * z);
            w /= n;
            x /= n;
            y /= n;
            z /= n;
            return new Matrix3d(
                new GeoVector3d(1 - 2 * (y * y + z * z), 2 * (x * y + w * z), 2 * (x * z - w * y)),
                new GeoVector3d(2 * (x * y - w * z), 1 - 2 * (x * x + z * z), 2 * (y * z + w * x)),
                new GeoVector3d(2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y)));
        }
    }

    // Bounded Jacobi diagonalization of small real symmetric matrices, used by Horn's
    // quaternion absolute-orientation solution and point-distribution diagnostics.
    internal static class SymmetricEigen
    {
        internal static void Solve(double[,] source, out double[] values, out double[,] vectors)
        {
            int n = source.GetLength(0);
            var a = (double[,])source.Clone();
            vectors = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                vectors[i, i] = 1;
            }

            for (int iter = 0; iter < 128; iter++)
            {
                int p = 0, q = 1;
                double largest = 0, scale = 0;
                for (int i = 0; i < n; i++)
                {
                    scale += Math.Abs(a[i, i]);
                    for (int j = i + 1; j < n; j++)
                    {
                        if (Math.Abs(a[i, j]) > largest)
                        {
                            largest = Math.Abs(a[i, j]);
                            p = i;
                            q = j;
                        }
                    }
                }

                if (largest <= Math.Max(1e-30, scale * 1e-15))
                {
                    break;
                }

                double angle = .5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]), c = Math.Cos(angle), s = Math.Sin(angle);
                double app = a[p, p], aqq = a[q, q], apq = a[p, q];
                for (int k = 0; k < n; k++)
                {
                    if (k != p && k != q)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = a[p, k] = c * akp - s * akq;
                        a[k, q] = a[q, k] = s * akp + c * akq;
                    }
                }

                a[p, p] = c * c * app - 2 * s * c * apq + s * s * aqq;
                a[q, q] = s * s * app + 2 * s * c * apq + c * c * aqq;
                a[p, q] = a[q, p] = 0;
                for (int k = 0; k < n; k++)
                {
                    double vkp = vectors[k, p], vkq = vectors[k, q];
                    vectors[k, p] = c * vkp - s * vkq;
                    vectors[k, q] = s * vkp + c * vkq;
                }
            }

            values = new double[n];
            for (int i = 0; i < n; i++)
            {
                values[i] = a[i, i];
            }
        }

        internal static double DistributionRatio(GeoVector3d[] points, double[] weights, GeoVector3d center)
        {
            var cov = new double[3, 3];
            for (int i = 0; i < points.Length; i++)
            {
                var d = points[i] - center;
                double[] a =
                {
                    d.X,
                    d.Y,
                    d.Z
                };
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        cov[r, c] += weights[i] * a[r] * a[c];
                    }
                }
            }

            // 使用第二/第一主轴分布比：共面三点仍有两个独立方向，不能按最小特征值拒绝。
            Solve(cov, out var values, out _);
            Array.Sort(values);
            return values[2] > 0 ? Math.Sqrt(Math.Max(0, values[1]) / values[2]) : 0;
        }

        internal static Matrix3d FitRotation(
            GeoVector3d[] from,
            GeoVector3d[] to,
            double[] weights,
            GeoVector3d fromCenter,
            GeoVector3d toCenter)
        {
            var s = new double[3, 3];
            for (int i = 0; i < from.Length; i++)
            {
                var a = from[i] - fromCenter;
                var b = to[i] - toCenter;
                double[] av =
                {
                    a.X,
                    a.Y,
                    a.Z
                }, bv =
                {
                    b.X,
                    b.Y,
                    b.Z
                };
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        s[r, c] += weights[i] * av[r] * bv[c];
                    }
                }
            }

            // Horn 的互协方差顺序为 from * to^T，最大特征向量是 (w,x,y,z)。
            // 四元数构造保证正向旋转，不能靠负比例拟合镜像模型。
            double xx = s[0, 0], xy = s[0, 1], xz = s[0, 2], yx = s[1, 0], yy = s[1, 1], yz = s[1, 2], zx = s[2, 0], zy = s[2, 1], zz = s[2, 2];
            var n = new double[,]
            {
                {
                    xx + yy + zz,
                    yz - zy,
                    zx - xz,
                    xy - yx
                },
                {
                    yz - zy,
                    xx - yy - zz,
                    xy + yx,
                    zx + xz
                },
                {
                    zx - xz,
                    xy + yx,
                    -xx + yy - zz,
                    yz + zy
                },
                {
                    xy - yx,
                    zx + xz,
                    yz + zy,
                    -xx - yy + zz
                }
            };
            Solve(n, out var eigen, out var v);
            int max = 0;
            for (int i = 1; i < 4; i++)
            {
                if (eigen[i] > eigen[max])
                {
                    max = i;
                }
            }

            return Matrix3d.FromQuaternion(v[0, max], v[1, max], v[2, max], v[3, max]);
        }
    }

    /// <summary>固定 ENU 原点到 Root 局部空间的双精度相似映射，不依赖 Unity Transform。</summary>
    public sealed class GeoReferenceFrame
    {
        readonly EnuTangentFrame tangent;
        internal Matrix3d Rotation { get; }
        internal GeoVector3d Translation { get; }
        internal double? MaximumDistance { get; }

        /// <summary>此不可变映射的 WGS84 绝对椭球高原点。</summary>
        public Wgs84Coordinate Origin => tangent.Origin;

        /// <summary>每现实米对应的 Root 局部单位数；不包含 Root 世界缩放。</summary>
        public double LocalUnitsPerMeter { get; }

        /// <summary>Root 局部空间中的固定参考东、北、上轴。</summary>
        public GeoAxes3d LocalAxes => new GeoAxes3d(Rotation.X, Rotation.Z, Rotation.Y);

        internal GeoReferenceFrame(
            EnuTangentFrame tangent,
            Matrix3d rotation,
            GeoVector3d translation,
            double scale,
            double? maximumDistance)
        {
            this.tangent = tangent;
            Rotation = rotation;
            Translation = translation;
            LocalUnitsPerMeter = scale;
            MaximumDistance = maximumDistance;
        }

        internal GeoVector3d ToEngine(GeoVector3d local) => Rotation.Inverse(local - Translation) / LocalUnitsPerMeter;
        internal bool CheckRange(GeoVector3d q, out GeoError error)
        {
            error = default;
            if (!q.IsFinite || !GeoMath.Finite(q.SqrMagnitude))
            {
                return GeoError.Fail(GeoErrorCode.InvalidNumber, "Position overflow.", out error);
            }

            return !MaximumDistance.HasValue || q.Magnitude <= MaximumDistance.Value ||
                GeoError.Fail(
                GeoErrorCode.OutOfConfiguredRange,
                "Position exceeds the configured ENU radius.",
                out error);
        }

        /// <summary>将 WGS84 椭球高转换为 Root 局部双精度位置，并检查范围；不读取 Root 世界姿态。</summary>
        public bool TryWgs84ToLocal(in Wgs84Coordinate input, out GeoVector3d localPosition, out GeoError error)
        {
            localPosition = default;
            if (!GeoMath.ValidateCoordinate(input.LatitudeDegrees, input.LongitudeDegrees, input.EllipsoidHeightMeters, out error))
            {
                return false;
            }

            var q = tangent.ToEngine(input);
            if (!CheckRange(q, out error))
            {
                return false;
            }

            var p = Translation + Rotation.Apply(q) * LocalUnitsPerMeter;
            if (!p.IsFinite)
            {
                return GeoError.Fail(GeoErrorCode.InvalidNumber, "Local position overflow.", out error);
            }

            localPosition = p;
            return true;
        }

        /// <summary>将 Root 局部双精度位置反算为 WGS84 椭球高，并检查范围。</summary>
        public bool TryLocalToWgs84(in GeoVector3d localPosition, out Wgs84Coordinate output, out GeoError error)
        {
            output = default;
            var q = ToEngine(localPosition);
            return CheckRange(q, out error) && tangent.TryFromEngine(q, out output, out error);
        }

        /// <summary>将固定 ENU 东、北、上现实米偏移转换为局部位移，不包含平移项。</summary>
        public bool TryMetersOffsetToLocal(in GeoEnuOffset offset, out GeoVector3d localDisplacement, out GeoError error)
        {
            localDisplacement = Rotation.Apply(offset.EngineVector) * LocalUnitsPerMeter;
            error = default;
            if (localDisplacement.IsFinite)
            {
                return true;
            }

            localDisplacement = default;
            return GeoError.Fail(GeoErrorCode.InvalidNumber, "Offset must be finite.", out error);
        }
    }
}
