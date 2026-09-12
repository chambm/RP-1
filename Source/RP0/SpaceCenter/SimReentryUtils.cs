using System;

namespace RP0
{
    /// <summary>
    /// Orbit maths for the simulation's reentry mode.
    /// <para>
    /// The normal "start in orbit" path can only place a vessel on a bound orbit whose periapsis
    /// is above the atmosphere, which makes it useless for testing whether a vehicle survives an
    /// entry. Reentry mode instead describes the <i>arriving</i> trajectory - elliptical (by
    /// apoapsis) or hyperbolic (by excess speed) - and drops the vessel straight onto the entry
    /// interface on the inbound leg, so the player does not have to hand-compute a mean anomaly
    /// or warp for hours from an apoapsis a million kilometres out.
    /// </para>
    /// </summary>
    public static class SimReentryUtils
    {
        /// <summary>How far above the atmosphere the entry interface sits by default.</summary>
        public const double DefaultEntryAltitudeMargin = 0d;

        /// <summary>Default number of seconds of coast before the interface, to let the player set attitude.</summary>
        public const double DefaultLeadTime = 30d;

        public struct EntryState
        {
            public double SMA;
            public double Ecc;
            /// <summary>Mean anomaly (rad) at which the vessel should be spawned.</summary>
            public double MeanAnomaly;
            /// <summary>Inertial speed (m/s) at the entry interface.</summary>
            public double EntrySpeed;
            /// <summary>Flight path angle (deg, negative = descending) at the entry interface.</summary>
            public double EntryFPA;
            /// <summary>Altitude (m) the vessel is actually spawned at, once lead time is applied.</summary>
            public double SpawnAltitude;
        }

        /// <summary>
        /// Builds the arriving trajectory and the spawn point on it.
        /// </summary>
        /// <param name="body">Body being entered.</param>
        /// <param name="peAlt">Periapsis altitude (m). May be negative or inside the atmosphere - that is the point.</param>
        /// <param name="apAlt">Apoapsis altitude (m). Ignored when <paramref name="vInf"/> is positive.</param>
        /// <param name="vInf">Hyperbolic excess speed (m/s). Zero or less means an elliptical arrival.</param>
        /// <param name="entryAlt">Entry interface altitude (m).</param>
        /// <param name="leadTime">Seconds of coast before the interface to spawn at.</param>
        /// <param name="state">The resulting orbit and spawn point.</param>
        /// <param name="error">Human readable reason the arrival is impossible, when this returns false.</param>
        public static bool TryComputeEntry(CelestialBody body, double peAlt, double apAlt, double vInf,
                                           double entryAlt, double leadTime, out EntryState state, out string error)
        {
            state = new EntryState();
            error = null;

            if (body == null)
            {
                error = "No body selected.";
                return false;
            }

            double mu = body.gravParameter;
            double radius = body.Radius;
            double rp = radius + peAlt;
            double re = radius + entryAlt;

            if (rp <= 0d)
            {
                error = "Periapsis is below the centre of the body.";
                return false;
            }
            if (re <= rp)
            {
                error = "The entry interface must be above the periapsis.";
                return false;
            }

            double sma, ecc;
            if (vInf > 0d)
            {
                // Specific orbital energy is v_inf^2 / 2, so a = -mu / v_inf^2 (negative, as required).
                sma = -mu / (vInf * vInf);
                ecc = 1d - rp / sma;    // sma < 0, so this is > 1
            }
            else
            {
                double ra = radius + apAlt;
                if (ra < re)
                {
                    error = "Apoapsis is below the entry interface; the vessel never reaches it.";
                    return false;
                }
                sma = (ra + rp) * 0.5d;
                ecc = (ra - rp) / (ra + rp);
            }

            if (ecc < 0d)
            {
                error = "Apoapsis is below periapsis.";
                return false;
            }

            // True anomaly at the interface, taken on the inbound (negative) branch.
            double semiLatus = sma * (1d - ecc * ecc);
            double cosTa = ecc > 1e-9 ? (semiLatus / re - 1d) / ecc : 1d;
            if (cosTa > 1d) cosTa = 1d;
            else if (cosTa < -1d) cosTa = -1d;
            double trueAnomaly = -Math.Acos(cosTa);

            double meanAnomaly = MeanAnomalyFromTrueAnomaly(trueAnomaly, ecc);

            // Back the spawn point up along the trajectory by the lead time. Mean anomaly advances
            // linearly with time on both conic types, so this is exact rather than an approximation.
            double meanMotion = Math.Sqrt(mu / Math.Abs(sma * sma * sma));
            double spawnMeanAnomaly = meanAnomaly - meanMotion * Math.Max(leadTime, 0d);

            state.SMA = sma;
            state.Ecc = ecc;
            state.MeanAnomaly = spawnMeanAnomaly;
            state.EntrySpeed = Math.Sqrt(Math.Max(mu * (2d / re - 1d / sma), 0d));
            state.EntryFPA = FlightPathAngleDeg(re, sma, mu, rp);
            state.SpawnAltitude = RadiusAtMeanAnomaly(spawnMeanAnomaly, sma, ecc) - radius;
            return true;
        }

        /// <summary>Mean anomaly for a true anomaly, on either an elliptical or a hyperbolic conic.</summary>
        public static double MeanAnomalyFromTrueAnomaly(double trueAnomaly, double ecc)
        {
            if (ecc < 1d)
            {
                double eccAnomaly = 2d * Math.Atan2(Math.Sqrt(1d - ecc) * Math.Sin(trueAnomaly * 0.5d),
                                                    Math.Sqrt(1d + ecc) * Math.Cos(trueAnomaly * 0.5d));
                return eccAnomaly - ecc * Math.Sin(eccAnomaly);
            }

            // Hyperbolic: H = 2 atanh( sqrt((e-1)/(e+1)) tan(nu/2) ), M = e sinh H - H
            double t = Math.Sqrt((ecc - 1d) / (ecc + 1d)) * Math.Tan(trueAnomaly * 0.5d);
            if (t >= 1d) t = 1d - 1e-12;
            else if (t <= -1d) t = -1d + 1e-12;
            double h = 2d * Atanh(t);
            return ecc * Math.Sinh(h) - h;
        }

        /// <summary>Radius at a mean anomaly, used only to report the spawn altitude back to the player.</summary>
        public static double RadiusAtMeanAnomaly(double meanAnomaly, double sma, double ecc)
        {
            if (ecc < 1d)
            {
                double e = SolveKepler(meanAnomaly, ecc);
                return sma * (1d - ecc * Math.Cos(e));
            }
            double h = SolveKeplerHyperbolic(meanAnomaly, ecc);
            return sma * (1d - ecc * Math.Cosh(h));
        }

        /// <summary>Flight path angle in degrees; negative because reentry mode always spawns inbound.</summary>
        private static double FlightPathAngleDeg(double r, double sma, double mu, double rp)
        {
            double speed = Math.Sqrt(Math.Max(mu * (2d / r - 1d / sma), 0d));
            double periSpeed = Math.Sqrt(Math.Max(mu * (2d / rp - 1d / sma), 0d));
            if (speed <= 0d) return 0d;
            double cosFpa = rp * periSpeed / (r * speed);    // angular momentum is conserved
            if (cosFpa > 1d) cosFpa = 1d;
            else if (cosFpa < -1d) cosFpa = -1d;
            return -Math.Acos(cosFpa) * (180d / Math.PI);
        }

        private static double SolveKepler(double meanAnomaly, double ecc)
        {
            double e = meanAnomaly;
            for (int i = 0; i < 40; ++i)
            {
                double f = e - ecc * Math.Sin(e) - meanAnomaly;
                double df = 1d - ecc * Math.Cos(e);
                if (Math.Abs(df) < 1e-12) break;
                double step = f / df;
                e -= step;
                if (Math.Abs(step) < 1e-12) break;
            }
            return e;
        }

        private static double SolveKeplerHyperbolic(double meanAnomaly, double ecc)
        {
            double h = Math.Sign(meanAnomaly) * Math.Log(2d * Math.Abs(meanAnomaly) / ecc + 1.8d);
            for (int i = 0; i < 60; ++i)
            {
                double f = ecc * Math.Sinh(h) - h - meanAnomaly;
                double df = ecc * Math.Cosh(h) - 1d;
                if (Math.Abs(df) < 1e-12) break;
                double step = f / df;
                h -= step;
                if (Math.Abs(step) < 1e-12) break;
            }
            return h;
        }

        private static double Atanh(double x)
        {
            return 0.5d * Math.Log((1d + x) / (1d - x));
        }
    }
}
