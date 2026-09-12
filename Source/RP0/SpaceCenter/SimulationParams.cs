using UniLinq;
using ROUtils.DataTypes;

namespace RP0
{
    public class SimulationParams : ConfigNodePersistenceBase, IConfigNode
    {
        public CelestialBody SimulationBody
        {
            get
            {
                if (_simulationBody == null)
                {
                    if (simulationBodyName != null && simulationBodyName != string.Empty)
                        _simulationBody = FlightGlobals.Bodies.FirstOrDefault(b => b.bodyName == simulationBodyName);
                }
                return _simulationBody;
            }
            set
            {
                _simulationBody = value;
                simulationBodyName = value.bodyName;
            }
        }

        private CelestialBody _simulationBody = null;

        [Persistent]
        private string simulationBodyName = string.Empty;

        [Persistent]
        public bool SimulateInOrbit, DisableFailures;
        public bool IsVesselMoved;
        [Persistent]
        public double SimulationUT, SimOrbitAltitude, SimOrbitPe, SimOrbitAp, SimInclination, SimLAN, SimMNA, SimArgPe;
        [Persistent]
        public int DelayMoveSeconds;

        /// <summary>
        /// Reentry mode: place the vessel on an arriving trajectory at the entry interface rather
        /// than on a safe parking orbit. Lifts the "periapsis must be above the atmosphere" clamp
        /// and allows hyperbolic arrivals, so a return from any orbit can actually be flown.
        /// </summary>
        [Persistent]
        public bool SimulateReentry;

        /// <summary>Hyperbolic excess speed (m/s). Zero means the arrival is elliptical and uses <see cref="SimOrbitAp"/>.</summary>
        [Persistent]
        public double SimEntryVInf;

        /// <summary>Altitude (m) of the entry interface the vessel is dropped onto.</summary>
        [Persistent]
        public double SimEntryAltitude;

        /// <summary>Seconds of coast before the entry interface, so the player can set attitude first.</summary>
        [Persistent]
        public double SimEntryLeadTime;

        /// <summary>Write per-tick thermal telemetry to a CSV, for checking the editor's reentry prediction against real flight.</summary>
        [Persistent]
        public bool LogReentryTelemetry;

        public void Reset()
        {
            IsVesselMoved = false;
        }
    }
}
