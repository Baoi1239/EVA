using UnityEngine;

/// <summary>First milestone: mirror one arm on top of the Animator's pose.</summary>
[DefaultExecutionOrder(200), DisallowMultipleComponent]
public sealed class EVAOneArmMirroring : MonoBehaviour
{
    [SerializeField] private EVAPoseReceiver receiver;
    [SerializeField] private EVAAnimationController animationController;
    [SerializeField] private bool mirrorHorizontal = true;
    [SerializeField] private bool avatarLeftArm = true;
    [SerializeField, Range(0f, 1f)] private float strength = 0.7f;
    [SerializeField, Range(0f, 1f)] private float minimumConfidence = 0.5f;
    [SerializeField, Min(0.05f)] private float fadeSeconds = 0.3f;
    [SerializeField, Range(10f, 180f)] private float maximumRotationDegrees = 120f;

    private Animator boundAnimator;
    private Transform upperArm, forearm, hand;
    private Vector3 sourceUpperAtCalibration, sourceForearmAtCalibration;
    private Vector3 avatarUpperAtCalibration, avatarForearmAtCalibration;
    private Vector3 sourceUpper, sourceForearm;
    private bool calibrated;
    private float weight;

    public bool IsCalibrated => calibrated;
    public float CurrentWeight => weight;

    private void Awake()
    {
        if (receiver == null) receiver = GetComponent<EVAPoseReceiver>();
        if (animationController == null) animationController = GetComponent<EVAAnimationController>();
    }

    private void LateUpdate()
    {
        Animator currentAnimator = animationController != null
            ? animationController.CurrentAnimator : null;
        if (currentAnimator != boundAnimator) BindRig(currentAnimator);
        if (currentAnimator == null || !currentAnimator.isActiveAndEnabled
            || !currentAnimator.isInitialized || upperArm == null || forearm == null || hand == null)
            return;

        bool hasPose = receiver != null && receiver.HasFreshPose
            && TryReadArm(receiver.LatestFrame);
        if (hasPose && !calibrated)
        {
            sourceUpperAtCalibration = sourceUpper;
            sourceForearmAtCalibration = sourceForearm;
            avatarUpperAtCalibration = (forearm.position - upperArm.position).normalized;
            avatarForearmAtCalibration = (hand.position - forearm.position).normalized;
            calibrated = true;
        }

        float targetWeight = hasPose && calibrated ? strength : 0f;
        weight = Mathf.MoveTowards(weight, targetWeight,
            Time.deltaTime / Mathf.Max(0.05f, fadeSeconds));
        if (!calibrated || weight <= 0f) return;

        Quaternion upperDelta = Quaternion.RotateTowards(Quaternion.identity,
            Quaternion.FromToRotation(sourceUpperAtCalibration, sourceUpper),
            maximumRotationDegrees);
        Vector3 upperTarget = upperDelta * avatarUpperAtCalibration;
        Vector3 animatedUpper = (forearm.position - upperArm.position).normalized;
        Quaternion upperRotation = Quaternion.FromToRotation(animatedUpper, upperTarget) * upperArm.rotation;
        upperArm.rotation = Quaternion.Slerp(upperArm.rotation, upperRotation, weight);

        Quaternion forearmDelta = Quaternion.RotateTowards(Quaternion.identity,
            Quaternion.FromToRotation(sourceForearmAtCalibration, sourceForearm),
            maximumRotationDegrees);
        Vector3 forearmTarget = forearmDelta * avatarForearmAtCalibration;
        Vector3 animatedForearm = (hand.position - forearm.position).normalized;
        Quaternion forearmRotation = Quaternion.FromToRotation(animatedForearm, forearmTarget) * forearm.rotation;
        forearm.rotation = Quaternion.Slerp(forearm.rotation, forearmRotation, weight);
    }

    private bool TryReadArm(EVAPoseFrame frame)
    {
        if (frame?.landmarks == null || frame.landmarks.Length != 33) return false;
        // In a visual mirror, the avatar's left arm follows the user's right arm.
        bool sourceLeft = avatarLeftArm != mirrorHorizontal;
        int shoulderIndex = sourceLeft ? 11 : 12;
        int elbowIndex = sourceLeft ? 13 : 14;
        int wristIndex = sourceLeft ? 15 : 16;
        var shoulder = frame.landmarks[shoulderIndex];
        var elbow = frame.landmarks[elbowIndex];
        var wrist = frame.landmarks[wristIndex];
        if (!Confident(shoulder) || !Confident(elbow) || !Confident(wrist)) return false;
        sourceUpper = ToUnity(elbow) - ToUnity(shoulder);
        sourceForearm = ToUnity(wrist) - ToUnity(elbow);
        if (sourceUpper.sqrMagnitude < 0.0001f || sourceForearm.sqrMagnitude < 0.0001f)
            return false;
        sourceUpper.Normalize();
        sourceForearm.Normalize();
        return true;
    }

    private bool Confident(EVAPoseLandmark point) => point != null
        && point.visibility >= minimumConfidence && point.presence >= minimumConfidence;

    private Vector3 ToUnity(EVAPoseLandmark point) =>
        new Vector3((mirrorHorizontal ? -1f : 1f) * point.x, -point.y, -point.z);

    private void BindRig(Animator animator)
    {
        boundAnimator = animator;
        upperArm = forearm = hand = null;
        calibrated = false;
        weight = 0f;
        if (animator == null) return;
        string side = avatarLeftArm ? "L" : "R";
        foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name == "Bip01 " + side + " UpperArm") upperArm = bone;
            else if (bone.name == "Bip01 " + side + " Forearm") forearm = bone;
            else if (bone.name == "Bip01 " + side + " Hand") hand = bone;
        }
        if (upperArm == null || forearm == null || hand == null)
            Debug.LogWarning("[EVA Mirroring] Arm bones missing on " + animator.name, this);
    }

    private void OnDisable()
    {
        calibrated = false;
        weight = 0f;
    }
}
