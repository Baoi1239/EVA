using UnityEngine;

/// <summary>Applies independently tracked segments after the Animator evaluates its base pose.</summary>
[DefaultExecutionOrder(200), DisallowMultipleComponent]
public sealed class EVAUpperBodyMirroring : MonoBehaviour
{
    [SerializeField] private EVAPoseReceiver receiver;
    [SerializeField] private EVAAnimationController animationController;
    [SerializeField] private bool mirrorHorizontal = true;
    [SerializeField, Range(0f, 1f)] private float strength = 0.7f;
    [SerializeField, Range(0f, 1f)] private float minimumConfidence = 0.5f;
    [SerializeField, Min(0.05f)] private float fadeSeconds = 0.3f;
    [SerializeField, Range(0f, .25f)] private float smoothingSeconds = .1f;
    [SerializeField] private Vector3 torsoLimits = new Vector3(30, 45, 20);
    [SerializeField] private float backwardLimit = 15;
    [SerializeField] private Vector3 headLimits = new Vector3(30, 60, 25);

    private class Segment
    {
        public bool tracked;
        public float weight;
        public void Blend(float strength, float step) =>
            weight = Mathf.MoveTowards(weight, tracked ? strength : 0f, step);
    }

    private sealed class Orientation : Segment
    {
        public Quaternion reference = Quaternion.identity, filtered = Quaternion.identity;
    }

    private struct ArmPose
    {
        public Vector3 upper, lower;
    }

    private sealed class Arm : Segment
    {
        public Transform clavicle, upper, lower, hand;
        public ArmPose neutral;
        public Vector3 wristTarget, elbowHint, pole, wristAxis;
        public float twistDegrees;
        public Quaternion upperCorrection, lowerCorrection;
        public bool initialized;
    }

    private Animator rig;
    private Transform spine, chest, neck, head;
    private Arm left, right;
    private Orientation torso, face;
    private Transform[] drivenBones;
    private Quaternion[] animatedLocals;
    private bool applied;
    private EVAPoseFrame previous;
    private long sampleStart;
    private int samples;
    private Quaternion observedTorso;

    public bool IsCalibrated { get; private set; }
    public string CalibrationStatus { get; private set; }
    public float Strength => strength;
    public float SmoothingSeconds => smoothingSeconds;
    public bool TracksLeftArm => IsCalibrated && left.tracked;
    public bool TracksRightArm => IsCalibrated && right.tracked;
    public bool TracksTorso => IsCalibrated && torso.tracked;
    public bool TracksHead => IsCalibrated && face.tracked;
    public void SetStrength(float value) => strength = Mathf.Clamp01(value);
    public void SetSmoothingSeconds(float value) => smoothingSeconds = Mathf.Clamp(value, 0f, .25f);

    public void RequestCalibration()
    {
        IsCalibrated = false;
        samples = 0;
        previous = null;
        CalibrationStatus = "Frontal stehen · Arme locker, Ellbogen leicht gebeugt";
        torso = new Orientation();
        face = new Orientation();
        observedTorso = Quaternion.identity;
        foreach (var arm in new[] { left, right })
        {
            if (arm == null) continue;
            arm.initialized = arm.tracked = false;
            arm.weight = 0;
            arm.twistDegrees = 0;
            arm.wristAxis = Vector3.down;
            arm.pole = Vector3.back;
        }
    }

    private void Awake()
    {
        if (!receiver) receiver = GetComponent<EVAPoseReceiver>();
        if (!animationController) animationController = GetComponent<EVAAnimationController>();
    }

    // Restore even bones for which the current clip has no rotation curve.
    private void Update() => RestoreAnimation();

    private void LateUpdate()
    {
        Animator active = animationController ? animationController.CurrentAnimator : null;
        if (active && (!active.isInitialized || !active.isActiveAndEnabled)) active = null;
        if (rig != active) Bind(active);
        if (!rig) return;

        Tick(receiver && receiver.HasFreshPose ? receiver.LatestFrame : null, Time.deltaTime);
    }

    private void Tick(EVAPoseFrame frame, float seconds)
    {
        if (frame == null)
        {
            torso.tracked = face.tracked = left.tracked = right.tracked = false;
            samples = 0;
        }
        else if (frame != previous)
        {
            float dt = previous == null ? 0 : (frame.sourceTimestampMs - previous.sourceTimestampMs) * .001f;
            if (dt <= 0 || dt > .5f) samples = 0;
            previous = frame;
            Observe(frame, dt > 0 && dt <= .5f ? dt : 1f / 30f);
        }
        if (!IsCalibrated) return;
        for (int i = 0; i < drivenBones.Length; i++)
            if (drivenBones[i]) animatedLocals[i] = drivenBones[i].localRotation;
        applied = true;
        float step = seconds / Mathf.Max(.05f, fadeSeconds);
        torso.Blend(strength, step);
        face.Blend(strength, step);
        Quaternion root = rig.transform.rotation;
        Quaternion trunkOffset = Quaternion.Slerp(Quaternion.identity, torso.filtered, torso.weight);
        SplitRotation(spine, chest, root, trunkOffset, .4f);
        Quaternion body = root * trunkOffset;
        SplitRotation(neck, head, body, Quaternion.Slerp(Quaternion.identity, face.filtered, face.weight), .3f);
        left.Blend(strength, step);
        right.Blend(strength, step);
        DriveArm(left, body, seconds);
        DriveArm(right, body, seconds);
    }

    private void Observe(EVAPoseFrame frame, float dt)
    {
        var points = frame.landmarks;
        torso.tracked = ReadTorso(points, out var trunk);
        if (torso.tracked) observedTorso = trunk;
        Quaternion inverse = Quaternion.Inverse(observedTorso);
        face.tracked = ReadHead(points, out var headWorld);
        Quaternion headRelative = inverse * headWorld;
        left.tracked = ReadArm(points, mirrorHorizontal ? 12 : 11, inverse, out var l);
        right.tracked = ReadArm(points, mirrorHorizontal ? 11 : 12, inverse, out var r);
        if (!IsCalibrated)
        {
            string missing = !torso.tracked ? "Schultern und Hüften" : !face.tracked ? "Nase und Ohren"
                : !left.tracked ? "Arm L" : !right.tracked ? "Arm R" : null;
            if (missing != null)
            {
                CalibrationStatus = "Kalibrierung · sichtbar machen: " + missing;
                samples = 0;
                return;
            }
            if (samples == 0) sampleStart = frame.sourceTimestampMs;
            float fraction = 1f / ++samples;
            torso.reference = Quaternion.Slerp(torso.reference, trunk, fraction);
            face.reference = Quaternion.Slerp(face.reference, headRelative, fraction);
            Accumulate(left, l, fraction);
            Accumulate(right, r, fraction);
            CalibrationStatus = "Eine Sekunde frontal stehen · Ellbogen leicht gebeugt";
            if (frame.sourceTimestampMs - sampleStart < 1000) return;
            if (!Usable(left.neutral.upper) || !Usable(left.neutral.lower)
                || !Usable(right.neutral.upper) || !Usable(right.neutral.lower))
            {
                samples = 0;
                CalibrationStatus = "Bitte beide Arme eine Sekunde ruhig halten";
                return;
            }
            PrepareArm(left, -1);
            PrepareArm(right, 1);
            IsCalibrated = true;
            CalibrationStatus = "Kalibriert";
        }
        float alpha = smoothingSeconds <= 0 ? 1 : 1 - Mathf.Exp(-dt / smoothingSeconds);
        if (torso.tracked) Filter(torso, Quaternion.Inverse(torso.reference) * trunk, torsoLimits, backwardLimit, alpha);
        if (face.tracked) Filter(face, headRelative * Quaternion.Inverse(face.reference), headLimits, headLimits.x, alpha);
        if (left.tracked) UpdateArm(left, l, alpha, dt);
        if (right.tracked) UpdateArm(right, r, alpha, dt);
    }

    private static void Filter(Orientation region, Quaternion delta, Vector3 limits, float backward, float alpha)
    {
        Vector3 a = delta.eulerAngles;
        var target = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0, a.x), -backward, limits.x),
            Mathf.Clamp(Mathf.DeltaAngle(0, a.y), -limits.y, limits.y),
            Mathf.Clamp(Mathf.DeltaAngle(0, a.z), -limits.z, limits.z));
        region.filtered = Quaternion.Slerp(region.filtered, target, alpha);
    }

    private static void SplitRotation(Transform first, Transform second, Quaternion space, Quaternion offset, float share)
    {
        if (first) first.rotation = space * Quaternion.Slerp(Quaternion.identity, offset, second ? share : 1)
            * Quaternion.Inverse(space) * first.rotation;
        if (second) second.rotation = space * Quaternion.Slerp(Quaternion.identity, offset, first ? 1-share : 1)
            * Quaternion.Inverse(space) * second.rotation;
    }

    private bool ReadTorso(EVAPoseLandmark[] points, out Quaternion orientation)
    {
        orientation = Quaternion.identity;
        if (!Point(points, 11, out var ls) || !Point(points, 12, out var rs)
            || !Point(points, 23, out var lh) || !Point(points, 24, out var rh)) return false;
        Vector3 across = mirrorHorizontal ? ls - rs : rs - ls;
        Vector3 up = (ls + rs - lh - rh) * 0.5f;
        if (!Usable(across) || !Usable(up)) return false;
        Vector3 forward = Vector3.Cross(across.normalized, up.normalized);
        if (!Usable(forward)) return false;
        orientation = Quaternion.LookRotation(forward, up);
        return true;
    }

    private bool ReadHead(EVAPoseLandmark[] points, out Quaternion orientation)
    {
        orientation = Quaternion.identity;
        if (!Point(points, 0, out var nose) || !Point(points, 7, out var le)
            || !Point(points, 8, out var re)) return false;
        Vector3 across = mirrorHorizontal ? le - re : re - le;
        if (!Usable(across)) return false;
        Vector3 forward = Vector3.ProjectOnPlane(nose - (le + re) * 0.5f, across);
        if (!Usable(forward)) return false;
        orientation = Quaternion.LookRotation(forward, Vector3.Cross(forward, across));
        return true;
    }

    private bool ReadArm(EVAPoseLandmark[] p, int side, Quaternion inverse, out ArmPose pose)
    {
        pose = default;
        if (!Point(p, side, out var shoulder) || !Point(p, side+2, out var elbow)
            || !Point(p, side+4, out var wrist)) return false;
        pose.upper = inverse * (elbow - shoulder);
        pose.lower = inverse * (wrist - elbow);
        return Usable(pose.upper) && Usable(pose.lower);
    }

    private static void Accumulate(Arm arm, ArmPose pose, float fraction)
    {
        arm.neutral.upper = Vector3.Lerp(arm.neutral.upper, pose.upper, fraction);
        arm.neutral.lower = Vector3.Lerp(arm.neutral.lower, pose.lower, fraction);
    }

    private static void PrepareArm(Arm arm, int side)
    {
        // Anatomical neutral axes in avatar space, independent of idle clip time.
        arm.upperCorrection = Quaternion.FromToRotation(arm.neutral.upper, new Vector3(.1f*side,-1,-.07f));
        arm.lowerCorrection = Quaternion.FromToRotation(arm.neutral.lower, new Vector3(.1f*side,-1,.2f));
    }

    private static void UpdateArm(Arm arm, ArmPose pose, float alpha, float dt)
    {
        if (!arm.upper || !arm.lower || !arm.hand) { arm.tracked = false; return; }
        float upper = Vector3.Distance(arm.upper.position, arm.lower.position);
        float lower = Vector3.Distance(arm.lower.position, arm.hand.position);
        Vector3 elbow = arm.upperCorrection * pose.upper * (upper / arm.neutral.upper.magnitude);
        Vector3 wrist = elbow + arm.lowerCorrection * pose.lower * (lower / arm.neutral.lower.magnitude);
        float blend = arm.initialized ? alpha : 1;
        arm.elbowHint = Vector3.Lerp(arm.elbowHint, elbow, blend);
        arm.wristTarget = Vector3.Lerp(arm.wristTarget, wrist, blend);
        arm.initialized = true;
        Vector3 axis = arm.wristTarget.normalized;
        if (axis.sqrMagnitude < .5f) return;
        Vector3 hint = Vector3.ProjectOnPlane(arm.elbowHint, axis);
        Vector3 transported = Quaternion.FromToRotation(arm.wristAxis, axis) * arm.pole;
        Vector3 previousPole = Vector3.ProjectOnPlane(transported, axis);
        arm.wristAxis = axis;
        if (previousPole.sqrMagnitude < .001f)
            previousPole = Vector3.ProjectOnPlane(Mathf.Abs(axis.y)<.8f ? Vector3.down : Vector3.back, axis);
        arm.pole = previousPole.normalized;
        if (hint.magnitude > .1f * upper)
        {
            float angle = Vector3.SignedAngle(arm.pole, hint, axis);
            arm.pole = Quaternion.AngleAxis(Mathf.Clamp(angle,-360*dt,360*dt), axis) * arm.pole;
        }
    }

    private static void DriveArm(Arm arm, Quaternion body, float seconds)
    {
        if (!arm.initialized || arm.weight <= 0) return;
        Vector3 direction = body * arm.wristTarget.normalized;
        if (direction.sqrMagnitude < .5f) return;
        Quaternion upperBase = arm.upper.localRotation, lowerBase = arm.lower.localRotation;
        Quaternion shoulderBase = arm.clavicle ? arm.clavicle.localRotation : Quaternion.identity;
        if (arm.clavicle)
        {
            Vector3 lateral = arm.upper.position - arm.clavicle.position;
            float lift = Mathf.Clamp01((Vector3.Angle(Vector3.down,arm.elbowHint)-60)/100);
            arm.clavicle.rotation = Quaternion.AngleAxis(20*lift, Vector3.Cross(lateral,body*Vector3.up).normalized)
                * arm.clavicle.rotation;
        }
        float a = Vector3.Distance(arm.upper.position, arm.lower.position);
        float b = Vector3.Distance(arm.lower.position, arm.hand.position);
        float Reach(float degrees) => Mathf.Sqrt(a*a+b*b+2*a*b*Mathf.Cos(degrees*Mathf.Deg2Rad));
        float d = Mathf.Clamp(arm.wristTarget.magnitude, Reach(150), Reach(5));
        float along = (a*a - b*b + d*d)/(2*d);
        Vector3 elbow = along*direction + Mathf.Sqrt(Mathf.Max(0,a*a-along*along))*(body*arm.pole);
        Vector3 forearm = d*direction-elbow;
        Vector3 oldUpper = arm.lower.position-arm.upper.position;
        Vector3 oldLower = arm.hand.position-arm.lower.position;
        Vector3 normal = Vector3.Cross(oldUpper.normalized, oldLower.normalized);
        if (normal.magnitude < .08f) normal = Vector3.Cross(oldUpper.normalized, body*Vector3.forward);
        Quaternion swing = Quaternion.FromToRotation(oldUpper, elbow);
        Vector3 targetNormal = Vector3.Cross(elbow,forearm);
        float twist = Vector3.SignedAngle(swing*normal,targetNormal,elbow);
        arm.twistDegrees = Mathf.MoveTowards(arm.twistDegrees, Mathf.Clamp(twist, -75, 75),
            240f * seconds);
        arm.upper.rotation = Quaternion.AngleAxis(arm.twistDegrees, elbow.normalized) * swing * arm.upper.rotation;
        arm.lower.rotation = Quaternion.FromToRotation(arm.hand.position-arm.lower.position,forearm)*arm.lower.rotation;
        arm.upper.localRotation = Quaternion.Slerp(upperBase,arm.upper.localRotation,arm.weight);
        arm.lower.localRotation = Quaternion.Slerp(lowerBase,arm.lower.localRotation,arm.weight);
        if (arm.clavicle) arm.clavicle.localRotation = Quaternion.Slerp(shoulderBase,arm.clavicle.localRotation,arm.weight);
    }

    private bool Point(EVAPoseLandmark[] points, int index, out Vector3 value)
    {
        value = Vector3.zero;
        if (points == null) return false;
        var p = points[index];
        if (p.visibility < minimumConfidence || p.presence < minimumConfidence) return false;
        value = new Vector3(mirrorHorizontal ? p.x : -p.x, -p.y, -p.z);
        return true;
    }

    private static bool Usable(Vector3 direction) =>
        direction.sqrMagnitude >= 0.0001f && !float.IsInfinity(direction.sqrMagnitude);

    private void Bind(Animator animator)
    {
        rig = animator;
        left = new Arm();
        right = new Arm();
        RequestCalibration();
        spine = chest = neck = head = null;
        if (!rig) return;
        Transform skeleton = rig.transform.Find("Bip01");
        if (skeleton)
        {
            foreach (var bone in skeleton.GetComponentsInChildren<Transform>(true))
            {
                switch (bone.name)
                {
                    case "Bip01 L Clavicle": left.clavicle = bone; break;
                    case "Bip01 R Clavicle": right.clavicle = bone; break;
                    case "Bip01 Neck": neck = bone; break;
                    case "Bip01 Spine2": chest = bone; break;
                    case "Bip01 Spine1": spine = bone; break;
                    case "Bip01 Head": head = bone; break;
                    case "Bip01 L UpperArm": left.upper = bone; break;
                    case "Bip01 L Forearm": left.lower = bone; break;
                    case "Bip01 L Hand": left.hand = bone; break;
                    case "Bip01 R UpperArm": right.upper = bone; break;
                    case "Bip01 R Forearm": right.lower = bone; break;
                    case "Bip01 R Hand": right.hand = bone; break;
                }
            }
        }
        drivenBones = new[] { spine, chest, neck, head, left.clavicle, right.clavicle, left.upper, left.lower, right.upper, right.lower };
        animatedLocals = new Quaternion[drivenBones.Length];
        if (!chest || !head || !left.upper || !left.lower || !left.hand ||
            !right.upper || !right.lower || !right.hand)
            Debug.LogWarning("[EVA Mirroring] Unvollständiges Oberkörper-Rig: " + rig.name, this);
    }

    private void RestoreAnimation()
    {
        if (!applied) return;
        for (int i = 0; i < drivenBones.Length; i++)
            if (drivenBones[i]) drivenBones[i].localRotation = animatedLocals[i];
        applied = false;
    }

    private void OnDisable()
    {
        RestoreAnimation();
        Bind(null);
    }
}
