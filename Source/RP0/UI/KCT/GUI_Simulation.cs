using System;
using System.Collections.Generic;
using UnityEngine;
using ROUtils;

namespace RP0
{
    public static partial class KCT_GUI
    {
        private static Rect _simulationWindowPosition = new Rect((Screen.width - 250) / 2, (Screen.height - 250) / 2, 250, 1);
        private static Rect _simulationConfigPosition = new Rect((Screen.width / 2) - 150, (Screen.height / 4), 300, 1);
        private static Vector2 _bodyChooserScrollPos;
        private static CelestialBody _bodyChooserRoot;
        private static Dictionary<CelestialBody, List<CelestialBody>> _bodyChooserChildren;

        private static string _sOrbitAlt = "", _sOrbitPe = "", _sOrbitAp = "", _sOrbitInc = "", _sOrbitLAN = "", _sOrbitMNA = "", _sOrbitArgPe = "", _UTString = "", _sDelay = "0";
        private static string _sEntryAlt = "", _sVInf = "", _sLeadTime = "30";
        private static bool _fromCurrentUT = true;
        private static bool _circOrbit = true;
        private static bool _reentryMode = false;
        private static bool _hyperbolicArrival = false;
        private static bool _logReentryTelemetry = false;

        public static void DrawSimulationWindow(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("This is a simulation.");
            GUILayout.Label("All progress will be lost after leaving the flight scene.");

            if (FlightDriver.CanRevertToPostInit && GUILayout.Button("Restart Simulation"))
            {
                GUIStates.ShowSimulationGUI = false;
                KCTUtilities.EnableSimulationLocks();
                FlightDriver.RevertToLaunch();
                SpaceCenterManagement.Instance.SimulationParams.Reset();
                _centralWindowPosition.height = 1;
            }

            if (FlightDriver.CanRevertToPrelaunch && GUILayout.Button("Revert to Editor"))
            {
                GUIStates.ShowSimulationGUI = false;
                KCTUtilities.DisableSimulationLocks();
                var facility = ShipConstruction.ShipType; // This uses stock behavior because the LaunchedVessel is no longer valid.
                FlightDriver.RevertToPrelaunch(facility);
                _centralWindowPosition.height = 1;
            }

            if (GUILayout.Button("Close"))
            {
                GUIStates.ShowSimulationGUI = !GUIStates.ShowSimulationGUI;
            }
            GUILayout.EndVertical();

            if (_simulationWindowPosition.width > 250)
                _simulationWindowPosition.width = 250;

            CenterWindow(ref _simulationWindowPosition);
        }

        public static void DrawSimulationConfigure(int windowID)
        {
            SimulationParams simParams = SpaceCenterManagement.Instance.SimulationParams;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Body: ");
            if (simParams == null) simParams = SpaceCenterManagement.Instance.SimulationParams = new SimulationParams();
            if (simParams.SimulationBody == null)
            {
                simParams.SimulationBody = Planetarium.fetch.Home;
            }
            GUILayout.Label(simParams.SimulationBody.bodyName);
            if (GUILayout.Button("Select", GUILayout.ExpandWidth(false)))
            {
                GUIStates.ShowSimConfig = false;
                GUIStates.ShowSimBodyChooser = true;
                _centralWindowPosition.height = 1;
                _simulationConfigPosition.height = 1;
            }
            GUILayout.EndHorizontal();
            if (simParams.SimulationBody == Planetarium.fetch.Home)
            {
                bool changed = simParams.SimulateInOrbit;
                simParams.SimulateInOrbit = GUILayout.Toggle(simParams.SimulateInOrbit, " Start in orbit");
                if (simParams.SimulateInOrbit != changed)
                    _simulationConfigPosition.height = 1;
            }
            if (simParams.SimulationBody != Planetarium.fetch.Home || simParams.SimulateInOrbit)
            {
                if (simParams.SimulationBody.atmosphere)
                {
                    bool wasReentry = _reentryMode;
                    _reentryMode = GUILayout.Toggle(_reentryMode, new GUIContent(" Reentry",
                        "Start on an arriving trajectory at the entry interface instead of a parking orbit. " +
                        "Periapsis may be inside the atmosphere and the arrival may be hyperbolic."));
                    if (_reentryMode != wasReentry)
                        _simulationConfigPosition.height = 1;
                }
                else
                {
                    _reentryMode = false;
                }

                if (_reentryMode)
                {
                    DrawReentryConfig(simParams.SimulationBody);
                }
                else
                {
                    _circOrbit = GUILayout.Toggle(_circOrbit, " Circular");
                    if (_circOrbit)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Orbit Altitude (km): ");
                        _sOrbitAlt = GUILayout.TextField(_sOrbitAlt, GUILayout.Width(100));
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Min: " + simParams.SimulationBody.atmosphereDepth / 1000 + "km");
                        GUILayout.Label("Max: " + Math.Floor((simParams.SimulationBody.sphereOfInfluence - simParams.SimulationBody.Radius) / 1000) + "km");
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Orbit Periapsis (km): ");
                        _sOrbitPe = GUILayout.TextField(_sOrbitPe, GUILayout.Width(100));
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Orbit Apoapsis (km): ");
                        _sOrbitAp = GUILayout.TextField(_sOrbitAp, GUILayout.Width(100));
                        GUILayout.EndHorizontal();
                    }
                }

                if (!simParams.SimulateInOrbit) simParams.SimulateInOrbit = true;
            }
            else
            {
                _reentryMode = false;
            }

            if (simParams.SimulateInOrbit)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Delay (s): ");
                _sDelay = GUILayout.TextField(_sDelay, 3, GUILayout.Width(40));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Inclination (degrees): ");
                _sOrbitInc = GUILayout.TextField(_sOrbitInc, GUILayout.Width(50));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("LAN (degrees): ");
                _sOrbitLAN = GUILayout.TextField(_sOrbitLAN, GUILayout.Width(50));
                GUILayout.EndHorizontal();

                if (!_reentryMode)
                {
                    // In reentry mode the mean anomaly is derived from the entry interface and lead time.
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Mean Anomaly (radians): ");
                    _sOrbitMNA = GUILayout.TextField(_sOrbitMNA, GUILayout.Width(50));
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Argument of Periapsis (degrees): ");
                _sOrbitArgPe = GUILayout.TextField(_sOrbitArgPe, GUILayout.Width(50));
                GUILayout.EndHorizontal();
            }

            string s1 = "Valid formats: \"1y 2d 3h 4m 5s\" and \"31719845\".";
            string s2 = "Valid formats: \"1y 2d 3h 4m 5s\", \"31719845\", and \"1960-12-31 23:59:59\".";

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Time: ", _fromCurrentUT ? s1 : s2));
            _UTString = GUILayout.TextField(_UTString, GUILayout.Width(110));
            _fromCurrentUT = GUILayout.Toggle(_fromCurrentUT, new GUIContent(" From Now", "If selected the game will warp forwards by the entered value. Otherwise the date and time will be set to the entered value."));
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (ModUtils.IsTestFlightInstalled || ModUtils.IsTestLiteInstalled)
            {
                simParams.DisableFailures = !GUILayout.Toggle(!simParams.DisableFailures, " Enable Part Failures (TestFlight or TestLite)");
                GUILayout.Space(4);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Simulate"))
            {
                StartSim(simParams);
            }

            if (GUILayout.Button("Cancel"))
            {
                GUIStates.ShowSimConfig = false;
                _centralWindowPosition.height = 1;
                _unlockEditor = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            CheckEditorLock();
            CenterWindow(ref _simulationConfigPosition);
        }

        /// <summary>
        /// The reentry half of the simulation config: describe the arriving trajectory rather than a
        /// parking orbit, and show what that actually means at the entry interface.
        /// </summary>
        private static void DrawReentryConfig(CelestialBody body)
        {
            if (string.IsNullOrEmpty(_sEntryAlt))
                _sEntryAlt = (GetDefaultEntryAltitude(body) / 1000d).ToString("F0");

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Periapsis (km): ", "Target periapsis of the arriving trajectory. This is the entry corridor setting; it may be inside the atmosphere or negative."));
            _sOrbitPe = GUILayout.TextField(_sOrbitPe, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            bool wasHyperbolic = _hyperbolicArrival;
            _hyperbolicArrival = GUILayout.Toggle(_hyperbolicArrival, new GUIContent(" Hyperbolic arrival",
                "Arriving faster than escape velocity, as on a lunar or interplanetary return. Set the excess speed instead of an apoapsis."));
            if (_hyperbolicArrival != wasHyperbolic)
                _simulationConfigPosition.height = 1;

            GUILayout.BeginHorizontal();
            if (_hyperbolicArrival)
            {
                GUILayout.Label(new GUIContent("Excess speed (m/s): ", "Hyperbolic excess speed (v-infinity) of the arrival. A lunar return to Earth is roughly 1100 m/s."));
                _sVInf = GUILayout.TextField(_sVInf, GUILayout.Width(100));
            }
            else
            {
                GUILayout.Label(new GUIContent("Apoapsis (km): ", "Apoapsis of the arriving orbit. Unlike a parking orbit this is not clamped to the sphere of influence."));
                _sOrbitAp = GUILayout.TextField(_sOrbitAp, GUILayout.Width(100));
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Entry interface (km): ", "Altitude the vessel is dropped onto, on the inbound leg."));
            _sEntryAlt = GUILayout.TextField(_sEntryAlt, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Lead time (s): ", "Coast this long before reaching the entry interface, to set attitude first."));
            _sLeadTime = GUILayout.TextField(_sLeadTime, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            _logReentryTelemetry = GUILayout.Toggle(_logReentryTelemetry, new GUIContent(" Log thermal telemetry",
                "Write per-tick heating data to a CSV in the save folder, for comparing a real entry against a predicted one."));

            // Live preview, so the player can see what the numbers they typed actually produce.
            var entry = default(SimReentryUtils.EntryState);
            bool ok = TryParseReentryInputs(out double peAlt, out double apAlt, out double vInf,
                                            out double entryAlt, out double leadTime, out string problem);
            if (ok)
                ok = SimReentryUtils.TryComputeEntry(body, peAlt, apAlt, vInf, entryAlt, leadTime, out entry, out problem);

            if (ok)
            {
                string conic = entry.Ecc < 1d ? "elliptical" : "hyperbolic";
                GUILayout.Label($"Entry: {entry.EntrySpeed:N0} m/s at {entry.EntryFPA:N2} deg FPA");
                GUILayout.Label($"{conic}, e={entry.Ecc:N4}, starts at {entry.SpawnAltitude / 1000d:N0} km");
            }
            else
            {
                GUILayout.Label(problem ?? "Invalid reentry setup");
            }
        }

        /// <summary>Parses the reentry text fields. Returns false, with a reason, if any of them is unusable.</summary>
        private static bool TryParseReentryInputs(out double peAlt, out double apAlt, out double vInf,
                                                  out double entryAlt, out double leadTime, out string error)
        {
            peAlt = apAlt = vInf = entryAlt = leadTime = 0d;
            error = null;

            if (!double.TryParse(_sOrbitPe, out peAlt))
            {
                error = "Enter a periapsis.";
                return false;
            }
            peAlt *= 1000d;

            if (_hyperbolicArrival)
            {
                if (!double.TryParse(_sVInf, out vInf) || vInf <= 0d)
                {
                    error = "Enter a positive excess speed.";
                    return false;
                }
            }
            else
            {
                if (!double.TryParse(_sOrbitAp, out apAlt))
                {
                    error = "Enter an apoapsis.";
                    return false;
                }
                apAlt *= 1000d;
            }

            if (!double.TryParse(_sEntryAlt, out entryAlt))
            {
                error = "Enter an entry interface altitude.";
                return false;
            }
            entryAlt *= 1000d;

            if (!double.TryParse(_sLeadTime, out leadTime) || leadTime < 0d)
            {
                error = "Enter a non-negative lead time.";
                return false;
            }

            return true;
        }

        public static void DrawBodyChooser(int windowID)
        {
            _bodyChooserScrollPos = GUILayout.BeginScrollView(_bodyChooserScrollPos, GUILayout.Height(500));
            GUILayout.BeginVertical();

            if (_bodyChooserChildren == null)
                BuildBodyChooserHierarchy();

            if (_bodyChooserRoot != null)
            {
                if (GUILayout.Button(_bodyChooserRoot.bodyName))
                    SelectSimBody(_bodyChooserRoot);

                if (_bodyChooserChildren.TryGetValue(_bodyChooserRoot, out var planets))
                {
                    foreach (CelestialBody planet in planets)
                    {
                        if (GUILayout.Button(planet.bodyName))
                            SelectSimBody(planet);

                        if (_bodyChooserChildren.TryGetValue(planet, out var moons))
                        {
                            foreach (CelestialBody moon in moons)
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.Space(20f);
                                if (GUILayout.Button(moon.bodyName))
                                    SelectSimBody(moon);
                                GUILayout.EndHorizontal();
                            }
                        }
                    }
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            CheckEditorLock();
            CenterWindow(ref _centralWindowPosition);
        }

        private static void BuildBodyChooserHierarchy()
        {
            _bodyChooserChildren = new Dictionary<CelestialBody, List<CelestialBody>>();
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (body.orbit == null)
                {
                    _bodyChooserRoot = body;
                }
                else
                {
                    var parent = body.referenceBody;
                    if (!_bodyChooserChildren.TryGetValue(parent, out var list))
                        _bodyChooserChildren[parent] = list = new List<CelestialBody>();
                    list.Add(body);
                }
            }
            foreach (List<CelestialBody> list in _bodyChooserChildren.Values)
            {
                list.Sort((a, b) => a.orbit.semiMajorAxis.CompareTo(b.orbit.semiMajorAxis));
            }
        }

        private static void SelectSimBody(CelestialBody body)
        {
            SpaceCenterManagement.Instance.SimulationParams.SimulationBody = body;
            GUIStates.ShowSimBodyChooser = false;
            GUIStates.ShowSimConfig = true;
            _centralWindowPosition.height = 1;
        }

        private static void StartSim(SimulationParams simParams)
        {
            if (SpaceCenterManagement.Instance.IsSimulatedFlight)
            {
                string msg = "Current save already appears to be a simulation. Starting a simulation inside a simulation isn't allowed.";
                PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "simErrorPopup", "Simulation error", msg, "Understood", false, HighLogic.UISkin).HideGUIsWhilePopup();
                return;
            }

            if (EditorLogic.fetch.ship.Count == 0)
            {
                var message = new ScreenMessage("Can't simulate without a vessel", 6f, ScreenMessageStyle.UPPER_CENTER);
                ScreenMessages.PostScreenMessage(message);
                return;
            }

            CelestialBody body = simParams.SimulationBody;

            if (body != Planetarium.fetch.Home)
                simParams.SimulateInOrbit = true;

            simParams.SimulateReentry = simParams.SimulateInOrbit && _reentryMode && body.atmosphere;

            if (simParams.SimulateInOrbit)
            {
                if (simParams.SimulateReentry)
                {
                    if (!TryParseReentryInputs(out double peAlt, out double apAlt, out double vInf,
                                               out double entryAlt, out double leadTime, out string parseError))
                    {
                        ScreenMessages.PostScreenMessage(new ScreenMessage(parseError, 6f, ScreenMessageStyle.UPPER_CENTER));
                        return;
                    }

                    if (!SimReentryUtils.TryComputeEntry(body, peAlt, apAlt, vInf, entryAlt, leadTime, out _, out string entryError))
                    {
                        ScreenMessages.PostScreenMessage(new ScreenMessage($"Cannot simulate that reentry: {entryError}", 6f, ScreenMessageStyle.UPPER_CENTER));
                        return;
                    }

                    // Deliberately unclamped: a periapsis inside the atmosphere is the whole point of this mode.
                    simParams.SimOrbitPe = peAlt;
                    simParams.SimOrbitAp = apAlt;
                    simParams.SimEntryVInf = vInf;
                    simParams.SimEntryAltitude = entryAlt;
                    simParams.SimEntryLeadTime = leadTime;
                    simParams.SimOrbitAltitude = 0;
                    simParams.LogReentryTelemetry = _logReentryTelemetry;
                }
                else
                {
                    if (_circOrbit)
                    {
                        if (!double.TryParse(_sOrbitAlt, out simParams.SimOrbitAltitude))
                            simParams.SimOrbitAltitude = GetDefaultAltitudeForBody(body);
                        else
                            simParams.SimOrbitAltitude = EnsureSafeMaxAltitude(1000 * simParams.SimOrbitAltitude, body);

                        simParams.SimOrbitPe = simParams.SimOrbitAp = 0;
                    }
                    else
                    {
                        if (!double.TryParse(_sOrbitPe, out simParams.SimOrbitPe))
                            simParams.SimOrbitPe = GetDefaultAltitudeForBody(body);

                        if (!double.TryParse(_sOrbitAp, out simParams.SimOrbitAp))
                            simParams.SimOrbitAp = GetDefaultAltitudeForBody(body);

                        simParams.SimOrbitAp = EnsureSafeMaxAltitude(1000 * simParams.SimOrbitAp, body);
                        simParams.SimOrbitPe = Math.Min(1000 * simParams.SimOrbitPe, simParams.SimOrbitAp);

                        simParams.SimOrbitAltitude = 0;
                    }
                }

                if (!double.TryParse(_sOrbitInc, out simParams.SimInclination))
                    simParams.SimInclination = 0;
                else
                    simParams.SimInclination %= 360;

                if (!double.TryParse(_sOrbitLAN, out simParams.SimLAN))
                    simParams.SimLAN = 0;
                else
                    simParams.SimLAN %= 360;

                if (!double.TryParse(_sOrbitMNA, out simParams.SimMNA))
                    simParams.SimMNA = Math.PI; // this will set it at apoapsis, good for safety
                else
                    simParams.SimMNA %= 2 * Math.PI;

                if (!double.TryParse(_sOrbitArgPe, out simParams.SimArgPe))
                    simParams.SimArgPe = 0;
                else
                    simParams.SimArgPe %= 360;
            }

            double currentUT = Planetarium.GetUniversalTime();
            double ut = 0;
            if (_fromCurrentUT && (_UTString.Contains("-") || _UTString.Contains(":"))) // prevent the user from doing 1960-12-31, accidentally selecting "From Now", and then warping 1960 years forward
            {
                var message = new ScreenMessage("Value must be of format \"1y 2d 3h 4m 5s\" or \"31719845\" when \"From Now\" is selected.", 6f, ScreenMessageStyle.UPPER_CENTER);
                ScreenMessages.PostScreenMessage(message);
                return;
            }
            else if (!string.IsNullOrWhiteSpace(_UTString) && 
                     ((_UTString.Contains(":") && !System.Text.RegularExpressions.Regex.IsMatch(_UTString, @"^\d{4}-\d{2}-\d{2}")) || 
                      !ROUtils.DTUtils.TryParseTimeString(_UTString, isTimespan: !_fromCurrentUT, out ut))) // if string is not empty and ((string has HH:mm but no YYYY-MM-DD) or (string fails TryParseTimeString)), then output failure
            {
                var message = new ScreenMessage("Please enter a valid time value.", 6f, ScreenMessageStyle.UPPER_CENTER);
                ScreenMessages.PostScreenMessage(message);
                return;
            }
            simParams.DelayMoveSeconds = 0;
            if (_fromCurrentUT)
            {
                simParams.SimulationUT = ut != 0 ? currentUT + ut : 0;
            }
            else
            {
                simParams.SimulationUT = ut;
            }

            if (simParams.SimulationUT < 0)
            {
                var message = new ScreenMessage("Cannot set time further back than the game start", 6f, ScreenMessageStyle.UPPER_CENTER);
                ScreenMessages.PostScreenMessage(message);
                return;
            }

            if (ModUtils.IsPrincipiaInstalled && simParams.SimulationUT != 0 && simParams.SimulationUT < currentUT + 0.5)
            {
                var message = new ScreenMessage("Going backwards in time isn't allowed with Principia", 6f, ScreenMessageStyle.UPPER_CENTER);
                ScreenMessages.PostScreenMessage(message);
                return;
            }

            int.TryParse(_sDelay, out simParams.DelayMoveSeconds);
            if (simParams.SimulationUT < 0)
                simParams.SimulationUT = currentUT;

            //_unlockEditor = true;
            GUIStates.ShowSimConfig = false;
            _centralWindowPosition.height = 1;
            string tempFile = KSPUtil.ApplicationRootPath + "saves/" + HighLogic.SaveFolder + "/Ships/temp.craft";
            KCTUtilities.MakeSimulationSave();

            // Create the LaunchedVessel fresh instead of cloning the EditorVessel, since it's possible that the player
            // may have changed the vessel slightly since the last time the coroutine updated the EditorVessel.
            SpaceCenterManagement.Instance.LaunchedVessel = new VesselProject(EditorLogic.fetch.ship, EditorLogic.fetch.launchSiteName, EditorLogic.FlagURL, true);
            // Just in case, let's set the LCID
            SpaceCenterManagement.Instance.LaunchedVessel.LCID = SpaceCenterManagement.EditorShipEditingMode ? SpaceCenterManagement.Instance.EditedVessel.LCID : SpaceCenterManagement.Instance.ActiveSC.ActiveLC.ID;

            VesselCrewManifest manifest = KSP.UI.CrewAssignmentDialog.Instance.GetManifest();
            if (manifest == null)
            {
                manifest = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(EditorLogic.fetch.ship.SaveShip(), null, true);
            }
            EditorLogic.fetch.ship.SaveShip().Save(tempFile);
            SpaceCenterManagement.Instance.IsSimulatedFlight = true;
            // EditorLogic.fetch.launchSiteName will always default to LaunchPad or Runway when entering the editor, and can only be changed to a different launch site in the editor,
            // So we can use SpaceCenterManagement.Instance.ActiveSC.ActiveLC.ActiveLPInstance.launchSiteName if it isn't changed
            // If it is changed inside of the editor, then SpaceCenterManagement.Instance.ActiveSC.ActiveLC.ActiveLPInstance.launchSiteName might not match it,
            // So we use EditorLogic.fetch.launchSiteName to align with expected behavior (launch at the site the user picks)

            // There also exists a seemingly stock bug where if a vessel is loaded automatically in one editor after entering it,
            // Then the user switches to the other editor, EditorLogic.fetch.launchSiteName won't run ValidLaunchSite() and will stay stuck as what it was before,
            // So we need to check for ValidLaunchSite() as well.
            // SpaceCenterManagement.Instance.ActiveSC.ActiveLC.ActiveLPInstance.launchSiteName is always valid, but that only applies to the VAB of course
            string launchSiteName = EditorLogic.fetch.launchSiteName;
            if ((launchSiteName == "LaunchPad" || !EditorDriver.ValidLaunchSite(launchSiteName)) && SpaceCenterManagement.Instance.ActiveSC.ActiveLC.LCType == LaunchComplexType.Pad)
            {
                launchSiteName = SpaceCenterManagement.Instance.ActiveSC.ActiveLC.ActiveLPInstance.launchSiteName;
            }
            else if (!EditorDriver.ValidLaunchSite(launchSiteName) && SpaceCenterManagement.Instance.ActiveSC.ActiveLC.LCType == LaunchComplexType.Hangar)
            {
                // If some mechanic is added to select a different default launch site for the SPH, then this will need to be updated to reflect that
                // For now, we can just use "Runway", since that's what the SPH always defaults to when one enters it
                launchSiteName = "Runway";
            }
            SpaceCenterManagement.Instance.StartCoroutine(CallbackUtil.DelayedCallback(1, delegate
            {
                FlightDriver.StartWithNewLaunch(tempFile, EditorLogic.FlagURL, launchSiteName, manifest);
            }));
        }

        private static double EnsureSafeMaxAltitude(double altitudeMeters, CelestialBody body)
        {
            return Math.Min(Math.Max(altitudeMeters, body.atmosphereDepth), body.sphereOfInfluence - body.Radius - 1000);
        }

        private static double GetDefaultAltitudeForBody(CelestialBody body)
        {
            return body.atmosphere ? body.atmosphereDepth + 30000 : 30000;
        }

        /// <summary>The top of the atmosphere is the natural entry interface; above it nothing happens.</summary>
        private static double GetDefaultEntryAltitude(CelestialBody body)
        {
            return body.atmosphere ? body.atmosphereDepth + SimReentryUtils.DefaultEntryAltitudeMargin : 0d;
        }
    }
}
