using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    public class Script
    {
        // ================================================================
        // DLG QA PLAN GENERATOR v0.9.2
        // ESAPI 16.1 / Windows Forms
        //
        // Automated generation of:
        //   - Synthetic phantom + Structure Set
        //   - QA Course + External Plan
        //   - Open reference field
        //   - MLC transmission A/B
        //   - User-selected sweeping gaps
        //
        // IMPORTANT:
        //   MU values are applied using CalculateDoseWithPresetValues().
        //   All fields are created as Sliding Window IMRT beams.
        //   OPEN/TX use a small monotonic MLC sweep entirely outside the
        //   X jaws, so the exposed geometry inside the jaw-defined field
        //   remains unchanged while satisfying Sliding Window validation.
        //
        // Changelog:
        //   v0.9.2 - Leaf-speed check before plan creation, geometry
        //            consistency check for edited constants, summary
        //            now includes OPEN/TX results, warning to close
        //            without saving after a partial generation,
        //            direction of hidden sweep based on jaw center.
        //   v0.9.1 - Null-safety check on CalculationResult, expanded
        //            documentation for non-obvious constants, removal
        //            of dead code. No change to clinical behavior.
        //   v0.9.0 - Multi-energy support, editable Machine ID,
        //            professional UI, automated MU verification.
        // ================================================================

        private const string APP_VERSION = "0.9.2";

        // Even though the generated beams are Sliding Window IMRT beams,
        // the technique identifier passed to ExternalBeamMachineParameters
        // remains "STATIC" in this Eclipse configuration. The Sliding
        // Window behavior is driven entirely by the control-point
        // meterset weights and leaf motion, not by this identifier.
        // Using "STATIC" here matches what Eclipse expects for the
        // underlying treatment-unit configuration and avoids validation
        // warnings during plan approval.
        private const string TECHNIQUE_ID = "STATIC";

        // Technical QA prescription required by Eclipse for preset-MU
        // photon dose calculation. This is not a clinical prescription.
        private const int QA_NUMBER_OF_FRACTIONS = 1;
        private const double QA_DOSE_PER_FRACTION_CGY = 100.0;
        private const double QA_TREATMENT_PERCENTAGE = 1.0;

        // ---------------- DEFAULT MU TARGETS ----------------
        // Defaults based on the current institutional workflow.
        // They remain editable in the user interface.
        private const int DEFAULT_MU_OPEN = 100;
        private const int DEFAULT_MU_DLG = 100;
        private const int DEFAULT_MU_TX = 100;

        // ---------------- PHANTOM ----------------
        private const int PHANTOM_X_PIXELS = 256;
        private const int PHANTOM_Y_PIXELS = 256;
        private const double PHANTOM_WIDTH_MM = 400.0;
        private const double PHANTOM_HEIGHT_MM = 400.0;
        private const int PHANTOM_PLANES = 81;
        private const double PHANTOM_PLANE_SEP_MM = 2.5;

        // ---------------- FIXED / VALIDATED GEOMETRY ----------------
        private const double X1 = -50.0;
        private const double X2 = +50.0;
        private const double Y1 = -90.0;
        private const double Y2 = +90.0;

        private const double OPEN_MLC_PADDING_MM = 10.0;
        private const double TX_OVERREACH_MM = 10.0;
        private const double TX_STRIP_WIDTH_MM = 1.0;

        // Small MLC motion applied only to OPEN/TX reference fields so
        // they remain physically unchanged inside the jaw aperture while
        // still satisfying Eclipse's Sliding Window validation.
        //
        // 2 mm was selected because:
        //   - it is below the leaf-position resolution that would affect
        //     the jaw-defined aperture (no leaf tip enters the field),
        //   - it is small enough to be clinically negligible (<< 1 mm
        //     dose perturbation inside the jaw aperture), and
        //   - it is large enough for Eclipse to register leaf motion
        //     during SW validation on all tested machine/energy
        //     combinations.
        private const double REFERENCE_HIDDEN_SWEEP_MM = 2.0;

        private const double SWEEP_START = -70.0;
        private const double SWEEP_END = +70.0;
        private const int N_STEPS = 11;

        private static readonly double[] AVAILABLE_GAPS =
            new double[] { 2, 4, 6, 10, 14, 16, 20 };

        // ---------------- MLC LIMITS ----------------
        // Maximum leaf speed allowed for the generated fields (mm/s at
        // the isocenter plane). 25 mm/s is a typical Eclipse value for
        // Millennium 120 / HD120. Check the value configured for your
        // MLC in RT Administration and adjust if needed.
        private const double MAX_LEAF_SPEED_MM_S = 25.0;

        // True once Patient.BeginModifications() has been called. Used to
        // warn the user to close the patient without saving if an error
        // occurs after generation has started.
        private bool modificationsStarted = false;

        public void Execute(ScriptContext context)
        {
            try
            {
                Run(context);
            }
            catch (Exception ex)
            {
                ShowError(ex, modificationsStarted);
            }
        }

        private void Run(ScriptContext context)
        {
            if (context.Patient == null)
            {
                MessageBox.Show(
                    "Open a QA patient before running the tool.",
                    "DLG QA Plan Generator",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            if (!context.Patient.CanModifyData())
                throw new ApplicationException(
                    "The current patient cannot be modified by ESAPI.");

            UserInput input = ConfigurationForm.ShowDialogAndGetInput();

            if (input == null)
                return;

            ValidateInput(input);

            if (!ShowGenerationConfirmation(input))
                return;

            context.Patient.BeginModifications();
            modificationsStarted = true;

            // ============================================================
            // PHANTOM + STRUCTURE SET
            // ============================================================
            string phantomError;

            if (!context.Patient.CanAddEmptyPhantom(out phantomError))
                throw new ApplicationException(
                    "ESAPI could not create the synthetic QA phantom.\r\n\r\n" +
                    phantomError);

            string phantomId = MakeUniquePhantomId();

            StructureSet ss = context.Patient.AddEmptyPhantom(
                phantomId,
                PatientOrientation.HeadFirstSupine,
                PHANTOM_X_PIXELS,
                PHANTOM_Y_PIXELS,
                PHANTOM_WIDTH_MM,
                PHANTOM_HEIGHT_MM,
                PHANTOM_PLANES,
                PHANTOM_PLANE_SEP_MM
            );

            if (ss == null || ss.Image == null)
                throw new ApplicationException(
                    "Eclipse did not return a valid Structure Set/Image.");

            VMS.TPS.Common.Model.API.Image image = ss.Image;

            // ============================================================
            // WATER BODY
            // Required so a photon dose calculation can be performed when
            // applying preset MU values.
            // ============================================================
            Structure body = CreateWaterBody(ss, image);

            // ============================================================
            // COURSE
            // ============================================================
            if (!context.Patient.CanAddCourse())
                throw new ApplicationException(
                    "Eclipse does not allow a new Course to be added.");

            Course course = context.Patient.AddCourse();

            string actualCourseId =
                MakeUniqueCourseId(
                    context.Patient,
                    input.CourseId,
                    course
                );

            course.Id = actualCourseId;

            // ============================================================
            // PLAN
            // ============================================================
            if (!course.CanAddPlanSetup(ss))
                throw new ApplicationException(
                    "Eclipse does not allow an ExternalPlanSetup " +
                    "for the generated Structure Set.");

            ExternalPlanSetup plan =
                course.AddExternalPlanSetup(ss);

            string actualPlanId =
                MakeUniquePlanId(
                    course,
                    input.PlanId,
                    plan
                );

            plan.Id = actualPlanId;
            plan.Name = "DLG QA Test";

            plan.Comment =
                SafeComment(
                    "DLG QA v" + APP_VERSION +
                    " | " + input.SelectedEnergy +
                    " | MU O/TX/G=" +
                    input.MuOpen + "/" +
                    input.MuTx + "/" +
                    input.MuDlg +
                    " | Jaws 100x180 mm" +
                    " | Sweep -70/+70 mm" +
                    " | Rx 1x100 cGy" +
                    " | preset MU");

            // ============================================================
            // ISOCENTER
            // ============================================================
            VVector isocenter = GetImageCenter(image);

            // ============================================================
            // MACHINE
            // ============================================================
            BeamEnergyConfiguration energyConfig =
                ResolveEnergyConfiguration(
                    input.SelectedEnergy);

            ExternalBeamMachineParameters machineParameters =
                new ExternalBeamMachineParameters(
                    input.MachineId,
                    energyConfig.EnergyModeId,
                    input.DoseRate,
                    TECHNIQUE_ID,
                    energyConfig.FluenceModeId
                );

            VRect<double> jaws =
                new VRect<double>(X1, Y1, X2, Y2);

            List<Beam> createdBeams =
                new List<Beam>();

            // ============================================================
            // OPEN REFERENCE
            // ============================================================
            string openId =
                MakeBeamId(input.FieldPrefix, "_OPEN");

            Beam openBeam = AddReferenceSlidingWindowBeam(
                plan,
                machineParameters,
                jaws,
                isocenter,
                openId,
                X1 - OPEN_MLC_PADDING_MM,
                X2 + OPEN_MLC_PADDING_MM
            );

            openBeam.Comment =
                SafeComment(
                    "DLG open reference | MU=" +
                    input.MuOpen +
                    " | MLC outside jaws");

            createdBeams.Add(openBeam);

            // ============================================================
            // TRANSMISSION A
            // ============================================================
            string txAId =
                MakeBeamId(input.FieldPrefix, "_TXA");

            double txACenter =
                X2 + TX_OVERREACH_MM;

            Beam txA = AddReferenceSlidingWindowBeam(
                plan,
                machineParameters,
                jaws,
                isocenter,
                txAId,
                txACenter - TX_STRIP_WIDTH_MM / 2.0,
                txACenter + TX_STRIP_WIDTH_MM / 2.0
            );

            txA.Comment =
                SafeComment(
                    "DLG transmission A | MU=" +
                    input.MuTx +
                    " | 1 mm strip @ +" +
                    txACenter.ToString("0.0") +
                    " mm");

            createdBeams.Add(txA);

            // ============================================================
            // TRANSMISSION B
            // ============================================================
            string txBId =
                MakeBeamId(input.FieldPrefix, "_TXB");

            double txBCenter =
                X1 - TX_OVERREACH_MM;

            Beam txB = AddReferenceSlidingWindowBeam(
                plan,
                machineParameters,
                jaws,
                isocenter,
                txBId,
                txBCenter - TX_STRIP_WIDTH_MM / 2.0,
                txBCenter + TX_STRIP_WIDTH_MM / 2.0
            );

            txB.Comment =
                SafeComment(
                    "DLG transmission B | MU=" +
                    input.MuTx +
                    " | 1 mm strip @ " +
                    txBCenter.ToString("0.0") +
                    " mm");

            createdBeams.Add(txB);

            // ============================================================
            // SWEEPING GAPS
            // ============================================================
            foreach (double gap in input.SelectedGaps)
            {
                string gapId =
                    MakeBeamId(
                        input.FieldPrefix,
                        "_G" + ((int)gap).ToString("00")
                    );

                Beam beam = AddDlgBeam(
                    plan,
                    machineParameters,
                    jaws,
                    isocenter,
                    gapId,
                    gap
                );

                beam.Comment =
                    SafeComment(
                        "DLG gap " +
                        gap.ToString("0") +
                        " mm | MU=" +
                        input.MuDlg +
                        " | sweep -70/+70 mm | 11 CP");

                createdBeams.Add(beam);
            }

            // ============================================================
            // APPLY PRESET MONITOR UNITS
            // ============================================================
            string calculationModel =
                ApplyPresetMonitorUnits(
                    plan,
                    input,
                    createdBeams,
                    openId,
                    txAId,
                    txBId
                );

            ShowSuccessSummary(
                input,
                course,
                plan,
                phantomId,
                ss,
                createdBeams,
                openId,
                txAId,
                txBId,
                calculationModel
            );
        }

        // ================================================================
        // CORE BEAM GENERATION
        // ================================================================

        private static Beam AddReferenceSlidingWindowBeam(
            ExternalPlanSetup plan,
            ExternalBeamMachineParameters machineParameters,
            VRect<double> jaws,
            VVector isocenter,
            string beamId,
            double bank0Position,
            double bank1Position)
        {
            // We already know that 11-point Sliding Window fields are valid
            // in this Eclipse configuration because the DLG beams use the
            // same control-point sequence successfully.
            List<double> metersetWeights =
                Enumerable.Range(0, N_STEPS)
                          .Select(
                              i =>
                                  (double)i /
                                  (N_STEPS - 1))
                          .ToList();

            Beam beam = plan.AddSlidingWindowBeam(
                machineParameters,
                metersetWeights,
                0.0,
                0.0,
                0.0,
                isocenter
            );

            beam.Id = beamId;

            BeamParameters p =
                beam.GetEditableParameters();

            // Both banks move in the same direction while maintaining the
            // same gap. The entire motion remains outside the jaw-defined
            // treatment aperture.
            double direction = 1.0;

            // For a field parked beyond the X1 jaw (TXB), move slightly
            // farther toward X1 instead of toward the jaw edge. Comparing
            // with the jaw center keeps this correct for asymmetric jaws.
            double center =
                (bank0Position + bank1Position) / 2.0;

            double jawCenter =
                (X1 + X2) / 2.0;

            if (center < jawCenter)
                direction = -1.0;

            for (
                int i = 0;
                i < p.ControlPoints.Count();
                i++)
            {
                ControlPointParameters cp =
                    p.ControlPoints.ElementAt(i);

                double fraction =
                    (double)i /
                    (p.ControlPoints.Count() - 1);

                double offset =
                    direction *
                    REFERENCE_HIDDEN_SWEEP_MM *
                    fraction;

                float[,] leaves =
                    cp.LeafPositions;

                int nLeafPairs =
                    leaves.GetLength(1);

                for (
                    int leaf = 0;
                    leaf < nLeafPairs;
                    leaf++)
                {
                    leaves[0, leaf] =
                        (float)(
                            bank0Position +
                            offset);

                    leaves[1, leaf] =
                        (float)(
                            bank1Position +
                            offset);
                }

                cp.LeafPositions = leaves;
                cp.JawPositions = jaws;
            }

            beam.ApplyParameters(p);

            return beam;
        }

        private static Beam AddDlgBeam(
            ExternalPlanSetup plan,
            ExternalBeamMachineParameters machineParameters,
            VRect<double> jaws,
            VVector isocenter,
            string beamId,
            double gapMm)
        {
            List<double> metersetWeights =
                Enumerable.Range(0, N_STEPS)
                          .Select(
                              i =>
                                  (double)i /
                                  (N_STEPS - 1))
                          .ToList();

            Beam beam = plan.AddSlidingWindowBeam(
                machineParameters,
                metersetWeights,
                0.0,
                0.0,
                0.0,
                isocenter
            );

            beam.Id = beamId;

            BeamParameters p =
                beam.GetEditableParameters();

            double step =
                (SWEEP_END - SWEEP_START) /
                (N_STEPS - 1);

            for (
                int i = 0;
                i < p.ControlPoints.Count();
                i++)
            {
                ControlPointParameters cp =
                    p.ControlPoints.ElementAt(i);

                double center =
                    SWEEP_START + i * step;

                double bank0 =
                    center - gapMm / 2.0;

                double bank1 =
                    center + gapMm / 2.0;

                float[,] leaves =
                    cp.LeafPositions;

                int nLeafPairs =
                    leaves.GetLength(1);

                for (
                    int leaf = 0;
                    leaf < nLeafPairs;
                    leaf++)
                {
                    leaves[0, leaf] =
                        (float)bank0;

                    leaves[1, leaf] =
                        (float)bank1;
                }

                cp.LeafPositions = leaves;
                cp.JawPositions = jaws;
            }

            beam.ApplyParameters(p);

            return beam;
        }

        // ================================================================
        // WATER PHANTOM BODY
        // ================================================================

        private static Structure CreateWaterBody(
            StructureSet ss,
            VMS.TPS.Common.Model.API.Image image)
        {
            const string bodyId = "BODY";

            if (!ss.CanAddStructure("EXTERNAL", bodyId))
                throw new ApplicationException(
                    "Could not add EXTERNAL/BODY to the generated phantom.");

            Structure body =
                ss.AddStructure(
                    "EXTERNAL",
                    bodyId);

            // Leave a 10 mm margin from the image boundary in X/Y and
            // one image plane at each end in Z.
            double xMin = 10.0;
            double yMin = 10.0;

            double xMax =
                (image.XSize - 1) *
                image.XRes -
                10.0;

            double yMax =
                (image.YSize - 1) *
                image.YRes -
                10.0;

            for (
                int z = 1;
                z < image.ZSize - 1;
                z++)
            {
                VVector[] contour =
                    new VVector[]
                    {
                        ImageOffsetPoint(
                            image,
                            xMin,
                            yMin,
                            z),

                        ImageOffsetPoint(
                            image,
                            xMax,
                            yMin,
                            z),

                        ImageOffsetPoint(
                            image,
                            xMax,
                            yMax,
                            z),

                        ImageOffsetPoint(
                            image,
                            xMin,
                            yMax,
                            z)
                    };

                body.AddContourOnImagePlane(
                    contour,
                    z);
            }

            string huError;

            if (body.CanSetAssignedHU(out huError))
                body.SetAssignedHU(0.0);

            return body;
        }

        private static VVector ImageOffsetPoint(
            VMS.TPS.Common.Model.API.Image image,
            double xDistance,
            double yDistance,
            int zPlane)
        {
            VVector o =
                image.Origin;

            VVector xd =
                image.XDirection;

            VVector yd =
                image.YDirection;

            VVector zd =
                image.ZDirection;

            double zDistance =
                zPlane *
                image.ZRes;

            return new VVector(
                o.x +
                xDistance * xd.x +
                yDistance * yd.x +
                zDistance * zd.x,

                o.y +
                xDistance * xd.y +
                yDistance * yd.y +
                zDistance * zd.y,

                o.z +
                xDistance * xd.z +
                yDistance * yd.z +
                zDistance * zd.z
            );
        }

        // ================================================================
        // MONITOR UNITS
        // ================================================================

        private static string ApplyPresetMonitorUnits(
            ExternalPlanSetup plan,
            UserInput input,
            List<Beam> beams,
            string openId,
            string txAId,
            string txBId)
        {
            // Eclipse requires prescription factors for the inverse-LMC /
            // preset-MU dose calculation. A simple technical QA prescription
            // is sufficient because the actual beam MU values are supplied
            // explicitly below.
            plan.SetPrescription(
                QA_NUMBER_OF_FRACTIONS,
                new DoseValue(
                    QA_DOSE_PER_FRACTION_CGY,
                    DoseValue.DoseUnit.cGy),
                QA_TREATMENT_PERCENTAGE
            );

            // A photon dose model is required because ESAPI applies preset
            // MU values through a dose calculation.
            string calculationModel =
                plan.GetCalculationModel(
                    CalculationType.PhotonVolumeDose);

            if (string.IsNullOrWhiteSpace(calculationModel))
            {
                List<string> models =
                    plan.GetModelsForCalculationType(
                        CalculationType.PhotonVolumeDose)
                        .Where(
                            m =>
                                !string.IsNullOrWhiteSpace(m))
                        .ToList();

                if (models.Count == 0)
                    throw new ApplicationException(
                        "No Photon Volume Dose calculation model is available " +
                        "for this plan. MU values could not be assigned.");

                // This dose is used only as the ESAPI mechanism for applying
                // preset MU values. The selected model is reported afterwards.
                calculationModel =
                    models.First();

                plan.SetCalculationModel(
                    CalculationType.PhotonVolumeDose,
                    calculationModel);
            }

            List<KeyValuePair<string, MetersetValue>> presetValues =
                new List<KeyValuePair<string, MetersetValue>>();

            foreach (Beam beam in beams)
            {
                double mu;

                if (beam.Id == openId)
                    mu = input.MuOpen;
                else if (
                    beam.Id == txAId ||
                    beam.Id == txBId)
                    mu = input.MuTx;
                else
                    mu = input.MuDlg;

                presetValues.Add(
                    new KeyValuePair<string, MetersetValue>(
                        beam.Id,
                        new MetersetValue(
                            mu,
                            DosimeterUnit.MU)
                    )
                );
            }

            CalculationResult result =
                plan.CalculateDoseWithPresetValues(
                    presetValues);

            if (result == null)
                throw new ApplicationException(
                    "Preset MU calculation returned a null result.\r\n\r\n" +
                    "Dose model: " +
                    calculationModel);

            if (!result.Success)
                throw new ApplicationException(
                    "Preset MU calculation failed.\r\n\r\n" +
                    "Dose model: " +
                    calculationModel +
                    "\r\n\r\n" +
                    result.ToString());

            // Read the values back from Eclipse. Do not assume assignment
            // succeeded just because the calculation returned success.
            foreach (Beam beam in beams)
            {
                double expected;

                if (beam.Id == openId)
                    expected = input.MuOpen;
                else if (
                    beam.Id == txAId ||
                    beam.Id == txBId)
                    expected = input.MuTx;
                else
                    expected = input.MuDlg;

                if (
                    beam.Meterset.Unit !=
                    DosimeterUnit.MU ||
                    Math.Abs(
                        beam.Meterset.Value -
                        expected) > 0.01)
                {
                    throw new ApplicationException(
                        "MU verification failed for beam " +
                        beam.Id +
                        ". Expected " +
                        expected.ToString("0.##") +
                        " MU, Eclipse returned " +
                        beam.Meterset.Value.ToString("0.##") +
                        " " +
                        beam.Meterset.Unit.ToString() +
                        ".");
                }
            }

            return calculationModel;
        }

        // ================================================================
        // VALIDATION / SUMMARY
        // ================================================================

        private static bool ValidateGap(
            Beam beam,
            double expectedGap)
        {
            foreach (
                ControlPoint cp
                in beam.ControlPoints)
            {
                float[,] leaves =
                    cp.LeafPositions;

                if (leaves.GetLength(1) == 0)
                    return false;

                int centralLeaf =
                    leaves.GetLength(1) / 2;

                double actualGap =
                    leaves[1, centralLeaf] -
                    leaves[0, centralLeaf];

                if (
                    Math.Abs(
                        actualGap -
                        expectedGap) > 0.01)
                    return false;
            }

            return true;
        }

        private static bool ValidateReferenceBeamOutsideJaws(
            Beam beam)
        {
            foreach (ControlPoint cp in beam.ControlPoints)
            {
                float[,] leaves =
                    cp.LeafPositions;

                if (leaves.GetLength(1) == 0)
                    return false;

                int centralLeaf =
                    leaves.GetLength(1) / 2;

                double bank0 =
                    leaves[0, centralLeaf];

                double bank1 =
                    leaves[1, centralLeaf];

                // OPEN: one edge must stay left of X1 and the other right
                // of X2. TX fields: both tips stay beyond the same X jaw.
                bool openLike =
                    bank0 <= X1 &&
                    bank1 >= X2;

                bool txRight =
                    bank0 > X2 &&
                    bank1 > X2;

                bool txLeft =
                    bank0 < X1 &&
                    bank1 < X1;

                if (
                    !openLike &&
                    !txRight &&
                    !txLeft)
                    return false;
            }

            return true;
        }

        private static bool ValidateCmw(
            Beam beam)
        {
            List<ControlPoint> cps =
                beam.ControlPoints.ToList();

            if (cps.Count != N_STEPS)
                return false;

            if (
                Math.Abs(
                    cps.First()
                       .MetersetWeight) > 1e-6)
                return false;

            if (
                Math.Abs(
                    cps.Last()
                       .MetersetWeight -
                    1.0) > 1e-6)
                return false;

            for (
                int i = 1;
                i < cps.Count;
                i++)
            {
                if (
                    cps[i].MetersetWeight <
                    cps[i - 1].MetersetWeight)
                    return false;
            }

            return true;
        }

        private static void ShowSuccessSummary(
            UserInput input,
            Course course,
            ExternalPlanSetup plan,
            string phantomId,
            StructureSet ss,
            List<Beam> createdBeams,
            string openId,
            string txAId,
            string txBId,
            string calculationModel)
        {
            StringBuilder sb =
                new StringBuilder();

            sb.AppendLine("Plan generation completed.");
            sb.AppendLine();
            sb.AppendLine(
                "Machine       " +
                input.MachineId);
            sb.AppendLine(
                "Energy        " +
                input.SelectedEnergy);
            sb.AppendLine(
                "Dose rate     " +
                input.DoseRate);
            sb.AppendLine(
                "Dose model    " +
                calculationModel);
            sb.AppendLine(
                "Prescription  1 x 100 cGy @ 100% (technical QA)");
            sb.AppendLine(
                "Course        " +
                course.Id);
            sb.AppendLine(
                "Plan          " +
                plan.Id);
            sb.AppendLine(
                "Phantom       " +
                phantomId);
            sb.AppendLine(
                "Structure Set " +
                ss.Id);
            sb.AppendLine();
            sb.AppendLine(
                "Generated beams: " +
                createdBeams.Count);
            sb.AppendLine();

            bool allGapOk = true;
            bool allCmwOk = true;
            bool allReferenceOk = true;

            foreach (Beam b in createdBeams)
            {
                if (
                    b.Id == openId ||
                    b.Id == txAId ||
                    b.Id == txBId)
                {
                    bool refGeometryOk =
                        ValidateReferenceBeamOutsideJaws(b);

                    bool refCmwOk =
                        ValidateCmw(b);

                    allReferenceOk =
                        allReferenceOk &&
                        refGeometryOk &&
                        refCmwOk;

                    sb.AppendLine(
                        "  [" +
                        (
                            refGeometryOk && refCmwOk
                            ? "OK"
                            : "CHECK"
                        ) +
                        "] " +
                        b.Id +
                        "   " +
                        b.Meterset.Value.ToString("0.##") +
                        " MU" +
                        "   outside-jaw=" +
                        (
                            refGeometryOk
                            ? "OK"
                            : "ERR"
                        ) +
                        "   CMW=" +
                        (
                            refCmwOk
                            ? "OK"
                            : "ERR"
                        ));
                }
                else
                {
                    double expectedGap =
                        ParseGapFromBeamId(b.Id);

                    bool gapOk =
                        ValidateGap(
                            b,
                            expectedGap);

                    bool cmwOk =
                        ValidateCmw(b);

                    allGapOk =
                        allGapOk &&
                        gapOk;

                    allCmwOk =
                        allCmwOk &&
                        cmwOk;

                    sb.AppendLine(
                        "  [" +
                        (
                            gapOk && cmwOk
                            ? "OK"
                            : "CHECK"
                        ) +
                        "] " +
                        b.Id +
                        "   MU=" +
                        b.Meterset.Value.ToString("0.##") +
                        "   gap=" +
                        (
                            gapOk
                            ? "OK"
                            : "ERR"
                        ) +
                        "   CMW=" +
                        (
                            cmwOk
                            ? "OK"
                            : "ERR"
                        )
                    );
                }
            }

            sb.AppendLine();
            sb.AppendLine(
                "Gap geometry: " +
                (
                    allGapOk
                    ? "PASS"
                    : "CHECK"
                ));

            sb.AppendLine(
                "Reference fields (OPEN/TX): " +
                (
                    allReferenceOk
                    ? "PASS"
                    : "CHECK"
                ));

            sb.AppendLine(
                "CMW sequence: " +
                (
                    allCmwOk
                    ? "PASS"
                    : "CHECK"
                ));

            sb.AppendLine();
            sb.AppendLine(
                "Assigned monitor units:");
            sb.AppendLine(
                "OPEN " +
                input.MuOpen +
                " MU | TX A/B " +
                input.MuTx +
                " MU | DLG gaps " +
                input.MuDlg +
                " MU");
            sb.AppendLine();
            sb.AppendLine(
                "Review the generated plan before irradiation.");

            ResultForm.ShowResult(
                "DLG QA Plan Generator",
                sb.ToString(),
                allGapOk && allCmwOk && allReferenceOk
            );
        }

        private static double ParseGapFromBeamId(
            string id)
        {
            int pos =
                id.LastIndexOf("_G");

            if (pos < 0)
                return -1;

            string text =
                id.Substring(pos + 2);

            double value;

            if (
                !double.TryParse(
                    text,
                    out value))
                return -1;

            return value;
        }

        // ================================================================
        // INPUT / CONFIRMATION
        // ================================================================

        private static void ValidateInput(
            UserInput input)
        {
            input.MachineId =
                string.IsNullOrWhiteSpace(input.MachineId)
                ? string.Empty
                : input.MachineId.Trim();

            if (string.IsNullOrWhiteSpace(input.MachineId))
                throw new ApplicationException(
                    "Machine ID is required. Enter the exact Treatment Unit ID used in Eclipse.");

            input.SelectedEnergy =
                CleanOrDefault(
                    input.SelectedEnergy,
                    "6X");

            input.CourseId =
                CleanOrDefault(
                    input.CourseId,
                    "DLG_QA");

            input.PlanId =
                CleanOrDefault(
                    input.PlanId,
                    "DLG_TEST");

            input.FieldPrefix =
                CleanOrDefault(
                    input.FieldPrefix,
                    "DLG");

            if (input.DoseRate <= 0)
                throw new ApplicationException(
                    "Dose rate must be greater than zero.");

            if (input.MuOpen <= 0 ||
                input.MuTx <= 0 ||
                input.MuDlg <= 0)
                throw new ApplicationException(
                    "All MU values must be greater than zero.");

            if (input.CourseId.Length > 16)
                throw new ApplicationException(
                    "Course ID must have at most 16 characters.");

            if (input.PlanId.Length > 16)
                throw new ApplicationException(
                    "Plan ID must have at most 16 characters.");

            if (input.FieldPrefix.Length > 10)
                throw new ApplicationException(
                    "Field prefix must have at most 10 characters.");

            if (
                input.SelectedGaps == null ||
                input.SelectedGaps.Count == 0)
                throw new ApplicationException(
                    "Select at least one sweeping gap.");

            // Geometry constants may have been edited by the user.
            ValidateGeometryConstants(input.SelectedGaps);

            // Leaf speed. Every leaf of a sweeping-gap field travels
            // SWEEP_END - SWEEP_START, whatever the gap. OPEN/TX leaves
            // travel only the hidden sweep.
            CheckLeafSpeed(
                "sweeping-gap",
                SWEEP_END - SWEEP_START,
                input.MuDlg,
                input.DoseRate);

            CheckLeafSpeed(
                "OPEN reference",
                REFERENCE_HIDDEN_SWEEP_MM,
                input.MuOpen,
                input.DoseRate);

            CheckLeafSpeed(
                "transmission A/B",
                REFERENCE_HIDDEN_SWEEP_MM,
                input.MuTx,
                input.DoseRate);
        }

        private static void CheckLeafSpeed(
            string fieldLabel,
            double travelMm,
            int mu,
            int doseRate)
        {
            // Minimum MU that keeps the leaf speed within the limit:
            //   time  = MU / (doseRate / 60)
            //   speed = travel / time
            //   speed <= limit  ->  MU >= travel * doseRate / (60 * limit)
            // The decision compares integers (MU), so there is no
            // floating-point ambiguity exactly at the limit.
            double minMu = Math.Ceiling(
                travelMm * doseRate / (60.0 * MAX_LEAF_SPEED_MM_S));

            if (mu >= minMu)
                return;

            double seconds = mu / (doseRate / 60.0);
            double speed = travelMm / seconds;

            throw new ApplicationException(
                "Leaf speed too high in the " + fieldLabel + " field(s).\r\n\r\n" +
                "Travel: " + travelMm.ToString("0.0") + " mm in " +
                seconds.ToString("0.0") + " s (" + mu + " MU at " +
                doseRate + " MU/min)\r\n" +
                "Leaf speed: " + speed.ToString("0.0") + " mm/s " +
                "(limit " + MAX_LEAF_SPEED_MM_S.ToString("0.0") + " mm/s)\r\n\r\n" +
                "Use at least " + minMu.ToString("0") + " MU for these fields " +
                "or reduce the dose rate.\r\n\r\n" +
                "The limit is set by MAX_LEAF_SPEED_MM_S. Check the value " +
                "configured for your MLC.");
        }

        // The checks below compare compile-time constants, so the C#
        // compiler reports "unreachable code" while the geometry is
        // consistent. The warning is expected and silenced here.
#pragma warning disable 0162
        private static void ValidateGeometryConstants(
            List<double> selectedGaps)
        {
            List<string> problems = new List<string>();

            if (X1 >= X2 || Y1 >= Y2)
                problems.Add("Jaw positions must satisfy X1 < X2 and Y1 < Y2.");

            if (N_STEPS < 2)
                problems.Add("N_STEPS must be at least 2.");

            // Sweeping gap must start fully behind X1 and end fully
            // behind X2, for the largest selected gap.
            double maxGap = selectedGaps.Max();

            if (SWEEP_START + maxGap / 2.0 > X1)
                problems.Add(
                    "At SWEEP_START the " + maxGap.ToString("0") +
                    " mm gap is not fully behind the X1 jaw.");

            if (SWEEP_END - maxGap / 2.0 < X2)
                problems.Add(
                    "At SWEEP_END the " + maxGap.ToString("0") +
                    " mm gap is not fully behind the X2 jaw.");

            // OPEN: the hidden sweep moves one leaf tip toward a jaw edge,
            // so the padding must be larger than the sweep.
            if (OPEN_MLC_PADDING_MM <= REFERENCE_HIDDEN_SWEEP_MM)
                problems.Add(
                    "OPEN_MLC_PADDING_MM must be larger than " +
                    "REFERENCE_HIDDEN_SWEEP_MM, otherwise a leaf tip " +
                    "can enter the jaw aperture.");

            // TX: the inner leaf tip must stay behind the jaw.
            if (TX_OVERREACH_MM - TX_STRIP_WIDTH_MM / 2.0 <= 0.0)
                problems.Add(
                    "TX_OVERREACH_MM must be larger than half of " +
                    "TX_STRIP_WIDTH_MM, otherwise the transmission strip " +
                    "enters the jaw aperture.");

            if (problems.Count > 0)
                throw new ApplicationException(
                    "Inconsistent geometry constants in DLGGenerator.cs:\r\n\r\n- " +
                    string.Join("\r\n- ", problems.ToArray()));
        }
#pragma warning restore 0162

        private static bool ShowGenerationConfirmation(
            UserInput input)
        {
            StringBuilder sb =
                new StringBuilder();

            sb.AppendLine(
                "A new QA phantom, Course and Plan will be created.");
            sb.AppendLine();
            sb.AppendLine(
                "Machine: " +
                input.MachineId);
            sb.AppendLine(
                "Energy: " +
                input.SelectedEnergy);
            sb.AppendLine(
                "Dose rate: " +
                input.DoseRate);
            sb.AppendLine(
                "Course: " +
                input.CourseId);
            sb.AppendLine(
                "Plan: " +
                input.PlanId);
            sb.AppendLine(
                "Field prefix: " +
                input.FieldPrefix);
            sb.AppendLine();
            sb.AppendLine(
                "Gaps: " +
                string.Join(
                    ", ",
                    input.SelectedGaps
                         .Select(
                             g =>
                                 g.ToString("0") +
                                 " mm")
                         .ToArray()));
            sb.AppendLine();
            sb.AppendLine(
                "Monitor units:");
            sb.AppendLine(
                "OPEN " + input.MuOpen +
                " MU | TX A/B " + input.MuTx +
                " MU | gaps " + input.MuDlg + " MU");
            sb.AppendLine();
            sb.AppendLine(
                "Fixed geometry:");
            sb.AppendLine(
                "Jaws 100 x 180 mm | DLG sweep -70 to +70 mm | 11 CP");
            sb.AppendLine(
                "OPEN/TX: 2 mm MLC motion outside jaws (no change inside aperture)");
            sb.AppendLine();
            sb.AppendLine(
                "Technical QA prescription: 1 fraction x 100 cGy at 100%.");
            sb.AppendLine(
                "MU values will be assigned automatically using preset MU dose calculation.");

            DialogResult result =
                MessageBox.Show(
                    sb.ToString(),
                    "Confirm DLG QA generation",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information
                );

            return
                result ==
                DialogResult.OK;
        }

        // ================================================================
        // GEOMETRY / IDS
        // ================================================================

        private static VVector GetImageCenter(
            VMS.TPS.Common.Model.API.Image image)
        {
            double halfX =
                (image.XSize - 1) *
                image.XRes /
                2.0;

            double halfY =
                (image.YSize - 1) *
                image.YRes /
                2.0;

            double halfZ =
                (image.ZSize - 1) *
                image.ZRes /
                2.0;

            VVector xd =
                image.XDirection;

            VVector yd =
                image.YDirection;

            VVector zd =
                image.ZDirection;

            VVector o =
                image.Origin;

            return new VVector(
                o.x +
                halfX * xd.x +
                halfY * yd.x +
                halfZ * zd.x,

                o.y +
                halfX * xd.y +
                halfY * yd.y +
                halfZ * zd.y,

                o.z +
                halfX * xd.z +
                halfY * yd.z +
                halfZ * zd.z
            );
        }

        private static BeamEnergyConfiguration ResolveEnergyConfiguration(
            string selectedEnergy)
        {
            string normalized =
                (selectedEnergy ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("-", "");

            switch (normalized)
            {
                case "6X":
                    return new BeamEnergyConfiguration(
                        "6X",
                        "");

                case "10X":
                    return new BeamEnergyConfiguration(
                        "10X",
                        "");

                case "15X":
                    return new BeamEnergyConfiguration(
                        "15X",
                        "");

                case "6XFFF":
                case "6FFF":
                    return new BeamEnergyConfiguration(
                        "6X",
                        "FFF");

                case "10XFFF":
                case "10FFF":
                    return new BeamEnergyConfiguration(
                        "10X",
                        "FFF");

                default:
                    throw new ApplicationException(
                        "Unsupported energy selection: " +
                        selectedEnergy +
                        ". Supported values: 6X, 10X, 15X, 6X FFF, 10X FFF.");
            }
        }

        private static int RecommendedDoseRate(
            string selectedEnergy)
        {
            string normalized =
                (selectedEnergy ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("-", "");

            // UI starting values only; the exact available dose rate is
            // treatment-unit specific and remains editable by the user.
            if (normalized == "6XFFF" ||
                normalized == "6FFF")
                return 1400;

            if (normalized == "10XFFF" ||
                normalized == "10FFF")
                return 2400;

            return 400;
        }

        private static string SafeComment(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            const int maxLength = 254;

            if (value.Length <= maxLength)
                return value;

            return value.Substring(
                0,
                maxLength);
        }

        private static string CleanOrDefault(
            string value,
            string defaultValue)
        {
            if (
                string.IsNullOrWhiteSpace(
                    value))
                return defaultValue;

            return value.Trim();
        }

        private static string MakeBeamId(
            string prefix,
            string suffix)
        {
            string id =
                prefix + suffix;

            if (id.Length > 16)
                throw new ApplicationException(
                    "Beam ID exceeds 16 characters: " +
                    id);

            return id;
        }

        private static string MakeUniquePhantomId()
        {
            return
                "DLGPH" +
                DateTime.Now
                        .ToString("HHmmss");
        }

        private static string MakeUniqueCourseId(
            Patient patient,
            string requestedId,
            Course newCourse)
        {
            string id =
                requestedId;

            int index = 1;

            while (
                patient.Courses.Any(
                    c =>
                        c != newCourse &&
                        string.Equals(
                            c.Id,
                            id,
                            StringComparison.OrdinalIgnoreCase)))
            {
                string suffix =
                    index.ToString();

                int maxBaseLength =
                    Math.Max(
                        1,
                        16 -
                        suffix.Length);

                string basePart =
                    requestedId.Length >
                    maxBaseLength
                    ? requestedId.Substring(
                        0,
                        maxBaseLength)
                    : requestedId;

                id =
                    basePart +
                    suffix;

                index++;
            }

            return id;
        }

        private static string MakeUniquePlanId(
            Course course,
            string requestedId,
            ExternalPlanSetup newPlan)
        {
            string id =
                requestedId;

            int index = 1;

            while (
                course.PlanSetups.Any(
                    p =>
                        p != newPlan &&
                        string.Equals(
                            p.Id,
                            id,
                            StringComparison.OrdinalIgnoreCase)))
            {
                string suffix =
                    index.ToString();

                int maxBaseLength =
                    Math.Max(
                        1,
                        16 -
                        suffix.Length);

                string basePart =
                    requestedId.Length >
                    maxBaseLength
                    ? requestedId.Substring(
                        0,
                        maxBaseLength)
                    : requestedId;

                id =
                    basePart +
                    suffix;

                index++;
            }

            return id;
        }

        private static void ShowError(
            Exception ex,
            bool modificationsStarted)
        {
            string message = ex.ToString();

            if (modificationsStarted)
                message =
                    "The QA plan was only partially generated.\r\n" +
                    "Close the patient WITHOUT saving to discard the " +
                    "incomplete phantom, course and plan.\r\n\r\n" +
                    "----------------------------------------\r\n\r\n" +
                    message;

            MessageBox.Show(
                message,
                "DLG QA Plan Generator - Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }

        // ================================================================
        // DATA MODEL
        // ================================================================

        private class BeamEnergyConfiguration
        {
            public string EnergyModeId { get; private set; }
            public string FluenceModeId { get; private set; }

            public BeamEnergyConfiguration(
                string energyModeId,
                string fluenceModeId)
            {
                EnergyModeId = energyModeId;
                FluenceModeId = fluenceModeId;
            }
        }

        private class UserInput
        {
            public string MachineId { get; set; }
            public string SelectedEnergy { get; set; }
            public int DoseRate { get; set; }
            public int MuOpen { get; set; }
            public int MuTx { get; set; }
            public int MuDlg { get; set; }
            public string CourseId { get; set; }
            public string PlanId { get; set; }
            public string FieldPrefix { get; set; }
            public List<double> SelectedGaps { get; set; }
        }

        // ================================================================
        // PROFESSIONAL CONFIGURATION FORM
        // ================================================================

        private class ConfigurationForm : Form
        {
            private TextBox machineBox;
            private ComboBox energyBox;
            private NumericUpDown doseRateBox;
            private NumericUpDown muOpenBox;
            private NumericUpDown muTxBox;
            private NumericUpDown muDlgBox;
            private TextBox courseBox;
            private TextBox planBox;
            private TextBox prefixBox;

            private Dictionary<double, CheckBox> gapChecks;

            private Button generateButton;
            private Button cancelButton;

            private Panel headerPanel;
            private Panel footerPanel;

            private readonly Color Navy =
                Color.FromArgb(
                    28,
                    48,
                    73);

            private readonly Color Accent =
                Color.FromArgb(
                    0,
                    120,
                    168);

            private readonly Color SoftBackground =
                Color.FromArgb(
                    245,
                    247,
                    250);

            public ConfigurationForm()
            {
                BuildForm();
            }

            public static UserInput ShowDialogAndGetInput()
            {
                using (
                    ConfigurationForm form =
                        new ConfigurationForm())
                {
                    DialogResult result =
                        form.ShowDialog();

                    if (
                        result !=
                        DialogResult.OK)
                        return null;

                    return form.GetInput();
                }
            }

            private void BuildForm()
            {
                Text =
                    "DLG QA Plan Generator";

                ClientSize =
                    new Size(
                        690,
                        755);

                StartPosition =
                    FormStartPosition.CenterScreen;

                FormBorderStyle =
                    FormBorderStyle.FixedDialog;

                MaximizeBox = false;
                MinimizeBox = false;

                BackColor =
                    SoftBackground;

                Font =
                    new Font(
                        "Segoe UI",
                        9F,
                        FontStyle.Regular);

                Icon = null;

                BuildHeader();
                BuildContent();
                BuildFooter();

                AcceptButton =
                    generateButton;

                CancelButton =
                    cancelButton;
            }

            private void BuildHeader()
            {
                headerPanel =
                    new Panel();

                headerPanel.Dock =
                    DockStyle.Top;

                headerPanel.Height = 92;
                headerPanel.BackColor =
                    Navy;

                Label title =
                    new Label();

                title.AutoSize = true;
                title.Left = 28;
                title.Top = 20;

                title.ForeColor =
                    Color.White;

                title.Font =
                    new Font(
                        "Segoe UI Semibold",
                        18F,
                        FontStyle.Bold);

                title.Text =
                    "DLG QA Plan Generator";

                Label subtitle =
                    new Label();

                subtitle.AutoSize = true;
                subtitle.Left = 30;
                subtitle.Top = 55;

                subtitle.ForeColor =
                    Color.FromArgb(
                        205,
                        220,
                        235);

                subtitle.Font =
                    new Font(
                        "Segoe UI",
                        9.5F);

                subtitle.Text =
                    "Automated MLC sweeping-gap plan generation for Eclipse";

                Label version =
                    new Label();

                version.AutoSize = true;
                version.Top = 27;
                version.Left = 595;

                version.ForeColor =
                    Color.White;

                version.BackColor =
                    Accent;

                version.Padding =
                    new Padding(
                        8,
                        3,
                        8,
                        3);

                version.Text =
                    "v" + APP_VERSION;

                headerPanel.Controls.Add(
                    title);

                headerPanel.Controls.Add(
                    subtitle);

                headerPanel.Controls.Add(
                    version);

                Controls.Add(
                    headerPanel);
            }

            private void BuildContent()
            {
                Panel content =
                    new Panel();

                content.Left = 22;
                content.Top = 105;
                content.Width = 630;
                content.Height = 555;
                content.BackColor =
                    SoftBackground;

                Controls.Add(content);

                GroupBox systemGroup =
                    NewGroup(
                        "Treatment unit",
                        0,
                        0,
                        302,
                        168);

                content.Controls.Add(
                    systemGroup);

                AddFieldLabel(
                    systemGroup,
                    "Machine ID",
                    18,
                    31);

                machineBox =
                    AddTextBox(
                        systemGroup,
                        "",
                        118,
                        27,
                        160,
                        16);

                AddFieldLabel(
                    systemGroup,
                    "Energy",
                    18,
                    73);

                energyBox =
                    new ComboBox();

                energyBox.Left = 118;
                energyBox.Top = 69;
                energyBox.Width = 160;
                energyBox.DropDownStyle =
                    ComboBoxStyle.DropDownList;

                energyBox.Items.AddRange(
                    new object[]
                    {
                        "6X",
                        "10X",
                        "15X",
                        "6X FFF",
                        "10X FFF"
                    });

                energyBox.SelectedIndex = 0;

                systemGroup.Controls.Add(
                    energyBox);

                AddFieldLabel(
                    systemGroup,
                    "Dose rate",
                    18,
                    115);

                doseRateBox =
                    new NumericUpDown();

                doseRateBox.Left = 118;
                doseRateBox.Top = 104;
                doseRateBox.Width = 160;
                doseRateBox.Minimum = 1;
                doseRateBox.Maximum = 2400;
                doseRateBox.Value = 400;
                doseRateBox.Increment = 50;

                systemGroup.Controls.Add(
                    doseRateBox);

                energyBox.SelectedIndexChanged +=
                    delegate
                    {
                        int suggested =
                            RecommendedDoseRate(
                                energyBox.SelectedItem == null
                                ? "6X"
                                : energyBox.SelectedItem.ToString());

                        if (suggested >= doseRateBox.Minimum &&
                            suggested <= doseRateBox.Maximum)
                            doseRateBox.Value = suggested;
                    };

                Label machineHelp =
                    new Label();

                machineHelp.Left = 18;
                machineHelp.Top = 135;
                machineHelp.Width = 260;
                machineHelp.Height = 24;
                machineHelp.ForeColor =
                    Color.DimGray;
                machineHelp.Font =
                    new Font(
                        "Segoe UI",
                        7.8F);
                machineHelp.Text =
                    "Machine ID must match Eclipse exactly.";

                systemGroup.Controls.Add(
                    machineHelp);

                GroupBox namingGroup =
                    NewGroup(
                        "Plan identification",
                        320,
                        0,
                        310,
                        168);

                content.Controls.Add(
                    namingGroup);

                AddFieldLabel(
                    namingGroup,
                    "Course ID",
                    18,
                    31);

                courseBox =
                    AddTextBox(
                        namingGroup,
                        "DLG_QA",
                        120,
                        27,
                        165,
                        16);

                AddFieldLabel(
                    namingGroup,
                    "Plan ID",
                    18,
                    73);

                planBox =
                    AddTextBox(
                        namingGroup,
                        "DLG_TEST",
                        120,
                        69,
                        165,
                        16);

                AddFieldLabel(
                    namingGroup,
                    "Field prefix",
                    18,
                    115);

                prefixBox =
                    AddTextBox(
                        namingGroup,
                        "DLG",
                        120,
                        111,
                        165,
                        10);

                GroupBox protocolGroup =
                    NewGroup(
                        "DLG protocol",
                        0,
                        180,
                        630,
                        125);

                content.Controls.Add(
                    protocolGroup);

                Label gapLabel =
                    new Label();

                gapLabel.Left = 18;
                gapLabel.Top = 27;
                gapLabel.AutoSize = true;

                gapLabel.Text =
                    "Sweeping gaps";

                gapLabel.ForeColor =
                    Navy;

                gapLabel.Font =
                    new Font(
                        "Segoe UI Semibold",
                        9F,
                        FontStyle.Bold);

                protocolGroup.Controls.Add(
                    gapLabel);

                gapChecks =
                    new Dictionary<double, CheckBox>();

                int x = 20;

                foreach (
                    double gap
                    in AVAILABLE_GAPS)
                {
                    CheckBox check =
                        new CheckBox();

                    check.Left = x;
                    check.Top = 52;
                    check.Width = 72;
                    check.Text =
                        gap.ToString("0") +
                        " mm";

                    check.Checked = true;

                    gapChecks.Add(
                        gap,
                        check);

                    protocolGroup.Controls.Add(
                        check);

                    x += 82;
                }

                Label includes =
                    new Label();

                includes.Left = 18;
                includes.Top = 86;
                includes.Width = 585;
                includes.Height = 30;

                includes.ForeColor =
                    Color.DimGray;

                includes.Text =
                    "Also generates: OPEN reference + MLC transmission A/B";

                protocolGroup.Controls.Add(
                    includes);

                GroupBox muGroup =
                    NewGroup(
                        "Monitor units",
                        0,
                        318,
                        630,
                        110);

                content.Controls.Add(
                    muGroup);

                Label muInfo =
                    new Label();

                muInfo.Left = 18;
                muInfo.Top = 21;
                muInfo.Width = 585;
                muInfo.Height = 22;
                muInfo.Text =
                    "Default protocol: 100 MU per field. Values remain editable.";
                muInfo.ForeColor =
                    Color.DimGray;
                muInfo.Font =
                    new Font(
                        "Segoe UI",
                        8.7F);

                muGroup.Controls.Add(
                    muInfo);

                // OPEN
                Label openMuLabel =
                    new Label();

                openMuLabel.Left = 35;
                openMuLabel.Top = 48;
                openMuLabel.Width = 120;
                openMuLabel.Text =
                    "Open reference";
                openMuLabel.TextAlign =
                    ContentAlignment.MiddleCenter;
                openMuLabel.ForeColor =
                    Navy;
                openMuLabel.Font =
                    new Font(
                        "Segoe UI Semibold",
                        9F,
                        FontStyle.Bold);

                muGroup.Controls.Add(
                    openMuLabel);

                muOpenBox =
                    AddMuBox(
                        muGroup,
                        DEFAULT_MU_OPEN,
                        42,
                        70);

                // TRANSMISSION
                Label txMuLabel =
                    new Label();

                txMuLabel.Left = 245;
                txMuLabel.Top = 48;
                txMuLabel.Width = 135;
                txMuLabel.Text =
                    "Transmission A/B";
                txMuLabel.TextAlign =
                    ContentAlignment.MiddleCenter;
                txMuLabel.ForeColor =
                    Navy;
                txMuLabel.Font =
                    new Font(
                        "Segoe UI Semibold",
                        9F,
                        FontStyle.Bold);

                muGroup.Controls.Add(
                    txMuLabel);

                muTxBox =
                    AddMuBox(
                        muGroup,
                        DEFAULT_MU_TX,
                        252,
                        70);

                // GAPS
                Label gapMuLabel =
                    new Label();

                gapMuLabel.Left = 455;
                gapMuLabel.Top = 48;
                gapMuLabel.Width = 120;
                gapMuLabel.Text =
                    "Sweeping gaps";
                gapMuLabel.TextAlign =
                    ContentAlignment.MiddleCenter;
                gapMuLabel.ForeColor =
                    Navy;
                gapMuLabel.Font =
                    new Font(
                        "Segoe UI Semibold",
                        9F,
                        FontStyle.Bold);

                muGroup.Controls.Add(
                    gapMuLabel);

                muDlgBox =
                    AddMuBox(
                        muGroup,
                        DEFAULT_MU_DLG,
                        462,
                        70);

                GroupBox geometryGroup =
                    NewGroup(
                        "Validated geometry",
                        0,
                        441,
                        630,
                        92);

                content.Controls.Add(
                    geometryGroup);

                Label geometry =
                    new Label();

                geometry.Left = 18;
                geometry.Top = 27;
                geometry.Width = 590;
                geometry.Height = 52;

                geometry.Font =
                    new Font(
                        "Consolas",
                        9.3F);

                geometry.ForeColor =
                    Color.FromArgb(
                        55,
                        65,
                        75);

                geometry.Text =
                    "Jaws       100 x 180 mm      DLG sweep  -70 -> +70 mm\r\n" +
                    "Control pts 11                Tx overreach 10 mm";

                geometryGroup.Controls.Add(
                    geometry);
            }

            private void BuildFooter()
            {
                footerPanel =
                    new Panel();

                footerPanel.Dock =
                    DockStyle.Bottom;

                footerPanel.Height = 78;
                footerPanel.BackColor =
                    Color.White;

                Label note =
                    new Label();

                note.Left = 25;
                note.Top = 15;
                note.Width = 390;
                note.Height = 48;

                note.ForeColor =
                    Color.FromArgb(
                        95,
                        95,
                        95);

                note.Text =
                    "QA / commissioning tool. Review all generated fields before use.\r\n" +
                    "MU values are applied automatically and verified after dose calculation.";

                footerPanel.Controls.Add(
                    note);

                generateButton =
                    new Button();

                generateButton.Text =
                    "Generate QA plan";

                generateButton.Left = 455;
                generateButton.Top = 20;
                generateButton.Width = 125;
                generateButton.Height = 36;

                generateButton.FlatStyle =
                    FlatStyle.Flat;

                generateButton.FlatAppearance.BorderSize =
                    0;

                generateButton.BackColor =
                    Accent;

                generateButton.ForeColor =
                    Color.White;

                generateButton.Font =
                    new Font(
                        "Segoe UI Semibold",
                        9.5F,
                        FontStyle.Bold);

                generateButton.DialogResult =
                    DialogResult.OK;

                cancelButton =
                    new Button();

                cancelButton.Text =
                    "Cancel";

                cancelButton.Left = 585;
                cancelButton.Top = 20;
                cancelButton.Width = 72;
                cancelButton.Height = 36;

                cancelButton.FlatStyle =
                    FlatStyle.Flat;

                cancelButton.BackColor =
                    Color.White;

                cancelButton.ForeColor =
                    Navy;

                cancelButton.DialogResult =
                    DialogResult.Cancel;

                footerPanel.Controls.Add(
                    generateButton);

                footerPanel.Controls.Add(
                    cancelButton);

                Controls.Add(
                    footerPanel);
            }

            private GroupBox NewGroup(
                string title,
                int left,
                int top,
                int width,
                int height)
            {
                GroupBox group =
                    new GroupBox();

                group.Text = title;
                group.Left = left;
                group.Top = top;
                group.Width = width;
                group.Height = height;

                group.BackColor =
                    Color.White;

                group.ForeColor =
                    Navy;

                group.Font =
                    new Font(
                        "Segoe UI Semibold",
                        9.5F,
                        FontStyle.Bold);

                return group;
            }

            private void AddFieldLabel(
                Control parent,
                string text,
                int left,
                int top)
            {
                Label label =
                    new Label();

                label.Left = left;
                label.Top = top;
                label.Width = 92;
                label.Text = text;

                label.ForeColor =
                    Color.FromArgb(
                        70,
                        78,
                        88);

                label.Font =
                    new Font(
                        "Segoe UI",
                        9F);

                parent.Controls.Add(
                    label);
            }

            private TextBox AddTextBox(
                Control parent,
                string value,
                int left,
                int top,
                int width,
                int maxLength)
            {
                TextBox box =
                    new TextBox();

                box.Left = left;
                box.Top = top;
                box.Width = width;
                box.Text = value;
                box.MaxLength = maxLength;

                parent.Controls.Add(
                    box);

                return box;
            }

            private NumericUpDown AddMuBox(
                Control parent,
                decimal value,
                int left,
                int top)
            {
                NumericUpDown box =
                    new NumericUpDown();

                box.Left = left;
                box.Top = top;
                box.Width = 105;
                box.Minimum = 1;
                box.Maximum = 5000;
                box.Value = value;
                box.Increment = 10;
                box.DecimalPlaces = 0;
                box.TextAlign =
                    HorizontalAlignment.Center;
                box.Font =
                    new Font(
                        "Segoe UI Semibold",
                        10F,
                        FontStyle.Bold);
                box.BackColor =
                    Color.White;
                box.ForeColor =
                    Color.FromArgb(
                        35,
                        45,
                        55);

                Label unit =
                    new Label();

                unit.Left = left + 110;
                unit.Top = top + 3;
                unit.Width = 30;
                unit.Text = "MU";
                unit.ForeColor =
                    Color.DimGray;
                unit.Font =
                    new Font(
                        "Segoe UI Semibold",
                        9F,
                        FontStyle.Bold);

                parent.Controls.Add(
                    box);

                parent.Controls.Add(
                    unit);

                return box;
            }

            private UserInput GetInput()
            {
                UserInput input =
                    new UserInput();

                input.MachineId =
                    machineBox.Text;

                input.SelectedEnergy =
                    energyBox.SelectedItem == null
                    ? "6X"
                    : energyBox.SelectedItem.ToString();

                input.DoseRate =
                    Convert.ToInt32(
                        doseRateBox.Value);

                input.MuOpen =
                    Convert.ToInt32(
                        muOpenBox.Value);

                input.MuTx =
                    Convert.ToInt32(
                        muTxBox.Value);

                input.MuDlg =
                    Convert.ToInt32(
                        muDlgBox.Value);

                input.CourseId =
                    courseBox.Text;

                input.PlanId =
                    planBox.Text;

                input.FieldPrefix =
                    prefixBox.Text;

                input.SelectedGaps =
                    gapChecks
                        .Where(
                            kv =>
                                kv.Value.Checked)
                        .Select(
                            kv =>
                                kv.Key)
                        .OrderBy(
                            x =>
                                x)
                        .ToList();

                return input;
            }
        }

        // ================================================================
        // PROFESSIONAL RESULT FORM
        // ================================================================

        private class ResultForm : Form
        {
            public static void ShowResult(
                string title,
                string text,
                bool success)
            {
                using (
                    ResultForm form =
                        new ResultForm(
                            title,
                            text,
                            success))
                {
                    form.ShowDialog();
                }
            }

            private ResultForm(
                string title,
                string text,
                bool success)
            {
                Text = title;

                Width = 650;
                Height = 610;

                StartPosition =
                    FormStartPosition.CenterScreen;

                FormBorderStyle =
                    FormBorderStyle.FixedDialog;

                MaximizeBox = false;
                MinimizeBox = false;

                BackColor =
                    Color.FromArgb(
                        245,
                        247,
                        250);

                Font =
                    new Font(
                        "Segoe UI",
                        9F);

                Panel header =
                    new Panel();

                header.Dock =
                    DockStyle.Top;

                header.Height = 80;

                header.BackColor =
                    success
                    ? Color.FromArgb(
                        35,
                        116,
                        83)
                    : Color.FromArgb(
                        173,
                        107,
                        0);

                Label heading =
                    new Label();

                heading.AutoSize = true;
                heading.Left = 24;
                heading.Top = 18;

                heading.ForeColor =
                    Color.White;

                heading.Font =
                    new Font(
                        "Segoe UI Semibold",
                        16F,
                        FontStyle.Bold);

                heading.Text =
                    success
                    ? "QA plan generated successfully"
                    : "QA plan generated - review required";

                Label sub =
                    new Label();

                sub.AutoSize = true;
                sub.Left = 26;
                sub.Top = 50;

                sub.ForeColor =
                    Color.White;

                sub.Text =
                    "Generation summary and automated geometry checks";

                header.Controls.Add(
                    heading);

                header.Controls.Add(
                    sub);

                Controls.Add(
                    header);

                TextBox summary =
                    new TextBox();

                summary.Left = 22;
                summary.Top = 100;
                summary.Width = 590;
                summary.Height = 405;

                summary.Multiline = true;
                summary.ReadOnly = true;
                summary.ScrollBars =
                    ScrollBars.Vertical;

                summary.Font =
                    new Font(
                        "Consolas",
                        9.2F);

                summary.BackColor =
                    Color.White;

                summary.Text = text;

                Controls.Add(
                    summary);

                Button close =
                    new Button();

                close.Text = "Close";
                close.Left = 515;
                close.Top = 525;
                close.Width = 95;
                close.Height = 34;

                close.DialogResult =
                    DialogResult.OK;

                close.FlatStyle =
                    FlatStyle.Flat;

                close.BackColor =
                    Color.FromArgb(
                        28,
                        48,
                        73);

                close.ForeColor =
                    Color.White;

                close.FlatAppearance.BorderSize =
                    0;

                Controls.Add(
                    close);

                AcceptButton = close;
            }
        }
    }
}
