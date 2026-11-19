using UnityEngine;
using UnityEngine.UI;

/// <summary>Orthographic comparison of incoming world landmarks and the animated rig.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EVAPoseDiagram : MaskableGraphic
{
    private static readonly int[] PoseEdges = {
        0,1, 1,2, 2,3, 3,7, 0,4, 4,5, 5,6, 6,8, 9,10, 11,12, 11,13, 13,15,
        15,17, 15,19, 15,21, 17,19, 12,14, 14,16, 16,18, 16,20, 16,22, 18,20,
        11,23, 12,24, 23,24, 23,25, 25,27, 27,29, 27,31, 29,31,
        24,26, 26,28, 28,30, 28,32, 30,32
    };
    private static readonly string[] BoneNames = {
        "Pelvis","Spine","Spine1","Spine2","Neck","Head",
        "L Clavicle","L UpperArm","L Forearm","L Hand",
        "R Clavicle","R UpperArm","R Forearm","R Hand",
        "L Thigh","L Calf","L Foot","L Toe0","R Thigh","R Calf","R Foot","R Toe0"
    };
    private static readonly int[] RigEdges = {
        0,1, 1,2, 2,3, 3,4, 4,5, 3,6, 6,7, 7,8, 8,9, 3,10, 10,11, 11,12, 12,13,
        0,14, 14,15, 15,16, 16,17, 0,18, 18,19, 19,20, 20,21
    };
    private readonly Vector2[] positions = new Vector2[33];
    private readonly bool[] visible = new bool[33];
    private readonly Transform[] bones = new Transform[BoneNames.Length];
    private EVAPoseReceiver receiver;
    private EVAAnimationController animations;
    private Animator rig;
    private bool side;

    public void Configure(EVAPoseReceiver source, EVAAnimationController avatar)
    {
        receiver = source;
        animations = avatar;
        raycastTarget = false;
    }

    public void SetSideView(bool value) { side = value; SetVerticesDirty(); }

    private void LateUpdate() => SetVerticesDirty();

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect area = rectTransform.rect;
        float scale = Mathf.Min(area.width * .24f, area.height * .46f);
        Vector2 origin = new Vector2(area.xMin + area.width * .25f, area.center.y);
        var points = receiver && receiver.HasFreshPose ? receiver.LatestFrame.landmarks : null;
        if (points != null)
        {
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                visible[i] = p.visibility >= .5f && p.presence >= .5f;
                positions[i] = origin + scale * new Vector2(side ? -p.z : -p.x, -p.y);
            }
            Draw(mesh, PoseEdges, points.Length, new Color32(84,221,247,255));
        }

        Animator current = animations ? animations.CurrentAnimator : null;
        if (rig != current)
        {
            rig = current;
            System.Array.Clear(bones, 0, bones.Length);
            Transform skeleton = rig ? rig.transform.Find("Bip01") : null;
            if (skeleton)
                foreach (var bone in skeleton.GetComponentsInChildren<Transform>(true))
                {
                    if (!bone.name.StartsWith("Bip01 ", System.StringComparison.Ordinal)) continue;
                    int index = System.Array.IndexOf(BoneNames, bone.name.Substring(6));
                    if (index >= 0) bones[index] = bone;
                }
        }
        if (!rig || !bones[0]) return;
        origin.x += area.width * .5f;
        for (int i = 0; i < bones.Length; i++)
        {
            visible[i] = bones[i] != null;
            if (!visible[i]) continue;
            Vector3 local = rig.transform.InverseTransformDirection(bones[i].position - bones[0].position);
            positions[i] = origin + scale * new Vector2(side ? local.z : -local.x, local.y);
        }
        Draw(mesh, RigEdges, bones.Length, new Color32(255,186,89,255));
    }

    private void Draw(VertexHelper mesh, int[] edges, int count, Color32 tint)
    {
        for (int i = 0; i < edges.Length; i += 2)
        {
            int a = edges[i], b = edges[i+1];
            if (!visible[a] || !visible[b]) continue;
            Vector2 start = positions[a], end = positions[b];
            Vector2 normal = Vector2.Perpendicular(end - start).normalized * 1.25f;
            Quad(mesh,start-normal,start+normal,end+normal,end-normal,tint);
        }
        for (int i = 0; i < count; i++)
        {
            if (!visible[i]) continue;
            Vector2 p = positions[i];
            Quad(mesh,p+Vector2.left*4,p+Vector2.up*4,p+Vector2.right*4,p+Vector2.down*4,Color.white);
        }
    }

    private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
    {
        int i = mesh.currentVertCount;
        mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
        mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
        mesh.AddTriangle(i,i+1,i+2); mesh.AddTriangle(i,i+2,i+3);
    }
}
